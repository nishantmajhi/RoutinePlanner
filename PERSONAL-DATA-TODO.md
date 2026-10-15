# Personal data in this repository — for the author

This is not about credentials. There are none in this project: `appsettings.json` holds no
connection string beyond a relative SQLite path, and there is no API key, token or password
anywhere in the sources or the configuration. That was checked.

It is about the two SQLite databases that were committed, one of which holds information about real,
identifiable people.

## What is in it

`src/RoutinePlanner.Web/App_Data/campus.sqlite3`, tracked in the initial commit, holds ten rows in a
`teachers` table. Each row is a real academic, named, with their title, the subject codes they teach,
and the hours they are available on each day of the week:

| | |
| --- | --- |
| People | 10, by name and title |
| Also recorded | subject allocations (8–9 course codes each), and weekly availability windows |
| Typical window | 10:30–13:30 and 14:00–17:00, Sunday to Friday |

The names are those of faculty at what appears to be Tribhuvan University's Central Department of
Computer Science and Information Technology, and the course codes match the MSc curriculum in
`App_Data/courses.json`. The names themselves are published in any faculty directory. The teaching
allocations and the daily working hours are the part that is not obviously public, and they are the
part that would be awkward to have published on somebody's behalf.

`src/RoutinePlanner.Web/App_Data/routine.sqlite3` was also tracked. It holds generated timetables,
which are derived from the above — so the same names, against specific periods of specific days.

## What has been done

Both files are now **untracked and gitignored**, and both are **still on disk, byte for byte**:

```
campus.sqlite3   8528dd0e6a2c295ae49146cc9acbc2ab6a437ff199621f4de797ffaa73210baa
routine.sqlite3  31823fb32f7f5303646baea135a8422532069ea980374b8f4766af857f98f85d
```

Nothing was deleted and nothing was edited. The local application keeps working against exactly the
data it had before.

In their place the repository now ships `App_Data/seed/teachers.seed.json` — ten invented teachers,
with the same shape of data and the real course codes, so a fresh clone can still generate a
timetable. It is loaded with:

```
dotnet run --project src/RoutinePlanner.Web -- --seed
```

It refuses to run if the teachers table already has rows, unless you add `--force`. JSON rather than
a database, because a change to a binary file is not reviewable in a diff.

## What is left, and why it is yours and not mine

**The data is still in this repository's git history, and that history is on a public remote** —
`https://github.com/nishantmajhi/RoutinePlanner.git`, commit `4297dba`, reachable from
`origin/main`. Untracking a file in a new commit does not withdraw what is already published. Anyone
who clones the repository gets `campus.sqlite3` in full by checking out that commit.

Rewriting history to remove it is irreversible, invalidates every existing clone and needs a force
push, so it is your call and not something a cleanup pass should do on its own. The options, briefly:

1. **Leave it.** Defensible if you judge the names to be public and the hours unremarkable. The new
   commit at least stops it spreading further and stops the working tree carrying it forward.
2. **Rewrite the history** with `git filter-repo --path src/RoutinePlanner.Web/App_Data/campus.sqlite3
   --path src/RoutinePlanner.Web/App_Data/routine.sqlite3 --invert-paths`, then force push. Bear in
   mind GitHub keeps unreachable objects accessible for a while, and forks and caches keep their own
   copies, so this reduces exposure rather than ending it.
3. **Make the repository private**, which is the cheapest thing that actually stops new access, and
   can be done before deciding about (2).

Whichever you pick, the people in that table have not been asked. That is the only reason this file
exists rather than a line in the README.

## Also worth a decision, while you are here

**There is no authentication on anything.** Every endpoint — the teacher list, add, update, remove,
and timetable generation — is open to anyone who can reach the port. Locally that is fine and is
plainly the intent. But the `Dockerfile` and the `$PORT` handling in `Program.cs` are there for
Render, and if this has ever been deployed then the teacher list was readable and editable by
anybody who found the URL. Nothing was added, because a login screen is a change to how the thing is
used and that is not mine to make; the README now says plainly that it must not be exposed. See
`review.md` for the shape of the smallest fix if you want one.

**There is no `LICENSE`.** Choosing one is yours.
