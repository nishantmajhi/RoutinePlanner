# RoutinePlanner

Generates a weekly class timetable. You tell it which days to use, when the day starts and ends,
where the break falls, how long a period is, and which subjects each section takes; it finds an
assignment of teachers to periods that satisfies everyone's availability, and saves the result as a
page you can print.

It is an ASP.NET Core 8 application with a static HTML/CSS/JS frontend and two SQLite files for
storage. There is no build step for the frontend and no database server to install.

```sh
dotnet run --project src/RoutinePlanner.Web -- --seed   # demo teachers, first time only
dotnet run --project src/RoutinePlanner.Web             # then http://localhost:3645
```

> **Do not put this on the open internet.** There is no authentication on any endpoint, including
> the ones that edit and delete teachers. See [Security](#security).

## The problem it solves

A timetable here is a grid: days across, periods down, and one cell per section in each period.
Filling it means choosing, for every (section, subject) pair, which teacher takes it and which
periods it occupies. The rules are:

- A subject gets exactly **`periods_per_subject`** periods a week, in one section.
- A subject appears **at most once per section per day**, so those periods land on different days.
- One **teacher per section per subject** for the whole week — a class does not get a different
  lecturer each time.
- A teacher takes **at most one subject in any one section**, so the load spreads across staff.
- A teacher is only scheduled **inside a window they said they were available**, and a period must
  fall entirely within one window. A window that ends halfway through a period does not count.
- A teacher is in **one place at a time** — sections run in parallel in the same period, taught by
  different people.

Those last three are why this is a search rather than a calculation. Two of the rules above also
give the arithmetic that decides feasibility before any search starts, which matters: see
[Why a request is refused](#why-a-request-is-refused).

## How generation works

`Services/Scheduling/` holds the engine, split by responsibility, and none of it touches HTTP or the
database.

The core is a **recursive backtracking search** over a flat list of assignments — one entry per
period of per subject of per section, grouped so that all the periods of one (section, subject) are
consecutive. For each entry it walks the day/period grid looking for a free cell, checks the rules
above, places the assignment, and recurses. When a branch dead-ends it undoes the placement and
tries the next cell.

Two points inject randomness: the order of the assignment groups is shuffled, and where several
teachers are equally eligible for a period the least-loaded three are the candidates and one is
picked at random. That means no two runs explore the space the same way, which makes the search
**embarrassingly parallel** — so `GenerateAsync` starts one independent attempt per core, each with
its own RNG, its own grid and its own bookkeeping, races them, keeps the first valid schedule any of
them finds, and cancels the rest. Nothing is shared between attempts, so there are no locks.

Consequences worth knowing:

- **The same request does not produce the same timetable twice.** That is deliberate — it is what
  lets the attempts cover different parts of the space — but it is reproducible when you want it to
  be. Every run writes the seed it derived from to its log in `logs/`, and setting
  `RoutinePlanner:Seed` to that value replays it exactly. `RoutinePlanner:ParallelAttempts` should
  be `1` if you want a single deterministic run, since otherwise the winner is whichever thread
  finished first.
- **The search is bounded by a clock.** `RoutinePlanner:GenerationTimeoutSeconds` (30 by default,
  `0` to disable) is a wall-clock ceiling on the whole race. Requests that are infeasible by
  arithmetic are refused before the search starts, but there is no way to decide in advance whether
  a particular combination of teachers, subjects and availability has a solution — so without a
  ceiling one request can hold every core for as long as the client keeps the connection open.
- **A teacher is identified by their database row, not their name.** Two rows may legitimately carry
  the same name. What the finished timetable shows is the name, because that is what a printed
  timetable needs; everything the engine decides is keyed on the id.

### Why a request is refused

`POST /generate` answers `400` with a message the page displays, rather than searching, whenever the
request cannot possibly work. Most of these are arithmetic:

| Refused when | Because |
| --- | --- |
| the day has no room for the periods asked for | the day's period count comes from `SlotGenerator`, the same code the search uses — so this figure and the grid always agree |
| `periods_per_subject` exceeds the number of days | a subject takes one period per day at most, so N periods need N different days |
| a section has no subjects, or the same subject twice | neither can be scheduled; a duplicate makes every complete schedule fail the engine's own completeness check |
| a day is listed twice | it would become two columns of the same name |
| a time, duration or count is the wrong shape | `"60"` is not `60` |
| a stored availability window is not `HH:MM` | and the message names the teacher whose row it is |

Note that the period count is **not** the available minutes divided by the period length. A period
may not straddle the break, so the morning and the afternoon each lose their own remainder: between
09:00 and 12:10 with a 10:10–11:00 break, 40-minute periods give **two** periods a day, not the
three that dividing 140 by 40 suggests.

If the request is possible but the search finds nothing, or hits the ceiling, the answer is a `500`
suggesting fewer periods per subject or more time. That is the honest answer — it means no timetable
was found, not that none exists.

## Running it

Needs the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). Visual Studio is not
required; the `dotnet` CLI does everything below.

```sh
dotnet run --project src/RoutinePlanner.Web
```

It listens on `http://0.0.0.0:3645` (`Urls` in `appsettings.json`), or on `$PORT` when that is set,
which is what container hosts do.

On a fresh clone there are no teachers, so generation has nobody to schedule. Load the demo set:

```sh
dotnet run --project src/RoutinePlanner.Web -- --seed
```

That reads `App_Data/seed/teachers.seed.json` — ten invented teachers with the real course codes
from `App_Data/courses.json` — and exits. It refuses to run if the teachers table already has rows,
unless you add `--force`, because the database it writes to is the one the app uses and those rows
may be real.

Then open `http://localhost:3645`, add or edit teachers, and use **Preferences** to generate.

### Installing the SDK somewhere other than `C:\`

The normal installer defaults to `C:\Program Files\dotnet` and pulls in more than you need. To put
just the SDK elsewhere:

```powershell
Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile dotnet-install.ps1
.\dotnet-install.ps1 -Channel 8.0 -InstallDir D:\dotnet
$env:DOTNET_ROOT = 'D:\dotnet'; $env:PATH = "D:\dotnet;$env:PATH"
```

## Storage

Two SQLite files under `src/RoutinePlanner.Web/App_Data/`, created on first use:

| File | Holds |
| --- | --- |
| `campus.sqlite3` | `teachers` — id, salutation, name, subject codes as JSON, availability as JSON |
| `routine.sqlite3` | `routine` — id, and each saved timetable as one JSON document |

**Neither is tracked in git,** and `.gitignore` excludes `*.sqlite3`. They hold live data: the one
that used to be committed contained real, named academics with their teaching loads and working
hours, which does not belong in a repository, and a binary file is not reviewable in a diff either.
`App_Data/seed/teachers.seed.json` is the reviewable replacement. See `PERSONAL-DATA-TODO.md`.

Two things follow from these being live files:

- **The app writes to the files under `App_Data/` directly.** `dotnet run` uses the project
  directory as its content root, so adding a teacher changes the file in your working tree. That is
  fine now that the files are untracked; before, it produced a binary diff and `git checkout` would
  silently discard real edits.
- **The build never overwrites a database it finds.** `CopyToOutputDirectory="PreserveNewest"`
  compares timestamps, not contents, and a fresh clone resets a file's timestamp to now — so the
  committed copy used to overwrite an already-mutated output copy on every rebuild. That was the
  original "routine not generating from the same sqlite db file" bug. The csproj instead copies each
  database into the output folder only if it is not already there;
  `tests/.../DatabaseBuildConfigurationTests.cs` and `PreserveNewestCopySemanticsTests.cs` hold that
  in place.

`courses.json` (the curriculum the frontend offers) and `courses.pdf` (the syllabus it came from)
are read-only reference data and are tracked.

## API

Every route the frontend uses. Request and response shapes are snake_case where the frontend sends
snake_case; the embedded routine JSON is camelCase because the page's script reads it that way.

| Route | Method | Notes |
| --- | --- | --- |
| `/`, `/style.css`, `/add/*`, `/edit/*`, … | GET | static files from `wwwroot/` |
| `/addTeacher`, `/editTeachers`, `/preferences` | GET | the same pages at tidier URLs |
| `/courses` | GET | `App_Data/courses.json` verbatim |
| `/teachers` | GET | `{ [id]: { teacherID, salutation, fullName, subjects_array, availability_json } }` |
| `/teacher/add` | POST | `{ salutation, name, subjects_array, availability_json }`; `name` is required, and subjects and availability are validated |
| `/teacher/get/{id}` | GET | one entry in the shape above |
| `/teacher/update` | PUT | `{ teacherID, ...fields }`; an omitted field is left alone, an explicit `null` clears it |
| `/teacher/remove/{id}` | DELETE | `{ success: true }` |
| `/generate` | POST | rate-limited to 5/minute per client; returns `{ id }` |
| `/routine`, `/routine/{id}` | GET | the latest or a specific timetable, rendered as a page |

`/routine` serves `wwwroot/routine/index.html` with `{{ID}}` filled in and the timetable appended as
`<script>const routine = {…};</script>` before the closing tag. That insertion is plain string
work in `Services/RoutinePageRenderer.cs`, deliberately — it used `Regex.Replace`, which interprets
`$` sequences in the replacement, and the replacement is where the serialized section and teacher
names go.

Validation errors come back as `{ "error": "..." }` and the frontend displays the text as-is, so
those messages are part of the interface.

## Running the tests

```sh
dotnet test
```

231 tests, xUnit. The parts worth knowing about:

- **`GenerateEndpointTests`, `TeacherEndpointTests`** drive the real application in-process through
  `WebApplicationFactory`, against a throwaway content root with its own empty databases. They
  assert on the status code and error text a *client* sees, which is where several defects showed:
  a request refusable by arithmetic was answered with a blanket `500` after an exhaustive search.
- **`GeneratedRoutineInvariantTests`** generates fifty randomly-shaped timetables and checks every
  rule in [The problem it solves](#the-problem-it-solves) against each one independently. The
  engine's own completeness check only counts periods and assumes the rest holds by construction, so
  this is the part that would notice if it did not. It also deliberately breaks each rule in turn to
  confirm the checker is capable of failing.
- **`TeacherIdentityTests`** covers two teachers who share a name, which the engine used to treat as
  one person.
- **`TempAppDirectory`** and **`RoutinePlannerTestApp`** give each test its own content root in the
  temp directory. Use one of them for anything that touches `App_Data`, never the real folder.

`Services/Scheduling/` has no framework, socket or database in it, which is what makes most of the
above testable without a host at all.

## Security

**There is no authentication anywhere.** No `[Authorize]`, no authentication middleware. Every
endpoint — read the teacher list, add, edit, delete, generate — is open to whoever can reach the
port. For a tool one person runs on their own machine that is a reasonable simplification and is
plainly what was intended.

The `Dockerfile` and the `$PORT` handling suggest deployment, though, and deployed this means anyone
who finds the URL can read the staff list and edit it. Nothing has been added, because a login is a
change to how the application is used; if you want one, the smallest version is an
`AddAuthentication`/`RequireAuthorization` pair in `Program.cs` plus a policy on the controllers, and
the teacher endpoints are the ones that matter most. Until then, run it locally or put it behind
something that authenticates.

What is in place: `/generate` is rate-limited to five requests a minute per client, the search has a
wall-clock ceiling, every SQL value is parameterised, the four writable teacher columns are checked
against an allowlist before they reach a `SET` clause, and the timetable JSON embedded in the routine
page is escaped by `System.Text.Json`'s default encoder, so a name containing `</script>` cannot
close the block.

## Deploying with Docker

The root `Dockerfile` is a multi-stage build — an SDK image publishes the app, a runtime-only image
runs it. The test project is not in the image.

```sh
docker build -t routine-planner .
docker run -p 8080:8080 routine-planner
```

On [Render](https://render.com): **New → Web Service**, connect the repo, and it detects the
`Dockerfile`; no build or start command is needed. Render injects `PORT` and `Program.cs` binds to
it.

**The caveat that matters:** Render's standard filesystem is ephemeral. Anything written inside the
container — including both `App_Data/*.sqlite3` files, which are this app's live database — is lost
on every redeploy and restart, and is not shared if you run more than one instance. Teacher edits
and generated timetables will not survive. Attach a [persistent disk](https://render.com/docs/disks)
mounted at `App_Data/`, or move to a managed database. This is a property of storing data in SQLite
files on an ephemeral host, not of anything specific here.

## History

This is a port of a Node/Express application, which is why so many files carry a comment naming the
`.js` file they came from. That original is not in this repository. The port kept the frontend and
the URL surface identical, and it fixed two bugs in `utils/validator.js` on the way:
`validateScheduleFeasibility` read a `subjects` field the frontend never sent, so `subjects.length`
threw; and the `periods` non-empty check tested `Array.isArray(periods)` where it meant
`!Array.isArray(...)`, making the check a no-op for the object the frontend actually sends.

`review.md` records what a later pass found and fixed, including what the review that prompted it
got wrong.

## Licence

Free for personal, non-commercial use. You may use, study and modify this
software for yourself, but you may not use it commercially, and you may not
share or publish it, in its original form or modified. See [LICENSE](LICENSE)
for the full terms.
