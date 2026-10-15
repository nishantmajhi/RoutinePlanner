# RoutinePlanner — review and remediation record

First reviewed 2026-09-04. Remediated 2026-09-26 on branch `cleanup/2026-09-26`. This file was a
to-do list; it is now a record of what was wrong, what was fixed, what was deliberately left, and
what is still the author's to decide.

## Correction to the first review, at the top because it sent the next reader to the wrong place

**The first review said: "There are no defects I can point at. The findings are about deployment and
data."**

That was wrong. There were ten defects in the business logic, and every one of them was reproduced
against the original code before being fixed. Two of them produce a wrong timetable; two hang the
search; four turn a client mistake into an unattributable `500`; one corrupts the rendered page; one
lets a request the server could refuse in microseconds occupy every core instead.

The review is not to blame for missing them by carelessness — it is to blame for a specific habit. It
read the structure, found it good, and inferred the behaviour from it. Its own words: "The structure
here is the most disciplined in the archive", "the test suite is the standout", "someone who writes a
`TempAppDirectory` helper has been burned by tests sharing state and fixed it properly". All true.
None of it is evidence about whether the code is correct. The 26 tests that were the basis for
"standout" covered path resolution, build configuration, JSON casing and repository round-trips —
the plumbing — and called the scheduling engine exactly once, in a three-assertion happy-path
integration test whose request has `break_end` at 12:00 and `end_time` at 09:00, which the
application's own validator rejects.

This is the same trap `Quadratic Parser/QIC` set, in a milder form: **a passing suite is evidence
about what it covers.** QIC's 152 tests covered a parser and never called the solver. This project's
26 covered the wiring and never exercised a constraint. The difference is only that QIC's review
claimed there were no tests, and this one praised the ones there were.

The three findings the review did raise (committed databases, no authentication, unsatisfiable
reporting) were all real and are all addressed below. Its finding 3 was the closest it came to the
engine, and it was right to ask the question — it just asked it as a thing to confirm rather than
looking.

## Live consequence, for the author

`src/RoutinePlanner.Web/App_Data/campus.sqlite3` was tracked and holds **ten real, named academics**
with their subject allocations and their weekly working hours. The repository has a public remote,
`github.com/nishantmajhi/RoutinePlanner`, and the data is in commit `4297dba` on `origin/main`.

Both databases are now untracked and gitignored, and both are still on disk byte for byte
(checksums in `PERSONAL-DATA-TODO.md`). That stops it spreading; it does not withdraw what is
published. **Rewriting the history is the author's decision** and is described in
`PERSONAL-DATA-TODO.md` along with the cheaper option of making the repository private.

## Defects found and fixed

Every one was confirmed failing against the original code first, in a git worktree of commit
`4297dba` — 14 assertions, all 14 failing there, all passing now.

### 1. Two teachers who share a name were one person to the engine — and the timetable said so

The grid recorded a teacher's **name**. Nothing requires a name to be unique; `POST /teacher/add`
never has. So:

- A (section, subject) lock stored the name and resolved it back with
  `teachers.Values.FirstOrDefault(t => t.Name == lockedTeacher)` — whichever row came first.
  **Availability was then checked against a different person from the one the lock was taken on.**
- `IsTeacherAvailableAtSlot` treated a teacher as busy because their namesake was teaching.
- `GetTeacherCurrentWorkload` charged one person's periods to the other, corrupting the tie-break.

The first of those produces a wrong answer, and the regression test prints it. Two rows called
"R. Sharma": row 1 teaches CSC539 and is free all week, row 2 teaches CSC538 and is free on Monday
only. CSC538 needs two periods, so one must fall on Tuesday, and only row 2 can teach it — so there
is no valid timetable. The original produced one anyway:

```
ORIGINAL PRODUCED A TIMETABLE for a request with no valid solution:
  Monday 08:00-09:00: CSC538 / R. Sharma
  Tuesday 08:00-09:00: CSC538 / R. Sharma
  Row 1 teaches CSC539 only and row 2 is not available on Tuesday.
```

A printed timetable putting a named person in a room at a time they said they were unavailable, for a
subject they do not teach.

**Fixed** by making the grid hold the teacher rather than their name. `Models/GridAssignment` is the
solver's cell type and carries the whole `Teacher`; `RoutineFormatter` projects it down to a
`SlotAssignment` with the name in it, so **the saved JSON and the page are byte-identical in shape**
— no frontend change, and `RoutineModelJsonCasingTests` still passes untouched. Locking, workload and
slot occupancy are all keyed on `Teacher.Id`. Resolving a lock is now a dictionary lookup rather than
a linear scan of every teacher at every node of the search, which is also a speed-up.

`TeacherIdentityTests`, 5 tests.

### 2. The feasibility check over-counted the periods in a day

`ValidateScheduleFeasibility` computed `(minutesBeforeBreak + minutesAfterBreak) / periodDuration`.
But a period may not straddle the break — `SlotGenerator` drops any period that would — so each half
of the day loses its own remainder, separately. A 09:00 start, a 10:10–11:00 break and a 12:10 end
with 40-minute periods: `floor(140 / 40)` is 3, and the grid has 2.

So a request for three periods was accepted as feasible against a grid with room for two, and the
answer arrived as a generic `500` after the search had exhausted a space with no solution in it.

**Fixed** by deleting the second calculation. The check now calls
`SlotGenerator.GenerateTimeSlots(...).Count` — the same code the search will use — so the two cannot
disagree. This is the archive's "generate the shared data from the implementation you have to agree
with" rule applied to a number rather than a table.

`RoutineRequestValidatorFeasibilityTests` pins the correspondence across six differently-shaped days.

### 3. `periods_per_subject` greater than the number of days was accepted

A subject gets at most one period per section per day, so N periods need N *distinct* days. Counting
total slots cannot see that: six periods of each subject across five days satisfies
`6 × subjects ≤ periodsPerDay × 5` comfortably and is still impossible.

Measured: one attempt thread, three sections of six subjects, **still searching after 20 seconds**
when the probe cut it off. Generation runs one attempt per core, `/generate` needs no authentication,
and the rate limit is five a minute.

**Fixed** with the missing inequality, and a message that explains the rule rather than quoting
arithmetic. Together with finding 2 the two necessary conditions are now both checked, which is
enough to make the day/period grid itself satisfiable — what remains undecidable up front is whether
the *teachers* can cover it, which is finding 4.

### 4. Nothing bounded the search

There was no timeout. A request that passes validation but has no solution — because of who teaches
what and when, which is not decidable in advance — held `Environment.ProcessorCount` threads at full
load for as long as the client kept the connection open.

**Fixed** with `RoutinePlanner:GenerationTimeoutSeconds`, 30 by default, `0` to disable, applied to
the linked token source that already existed for the attempt race. On expiry the response is the
existing "could not generate a valid schedule" `500`, unchanged, so no user-facing copy moved.

Also fixed alongside it: a cancelled request (client disconnected) now throws rather than returning
`null`, so "the client gave up" is no longer reported as "there is no timetable" — and, more to the
point, no longer saves a routine for a request nobody is waiting for.

`GenerationBoundsTests`, 8 tests, including that the ceiling does not cut short a search that was
going to succeed.

### 5. `JsonElement` accessors throw on the wrong JSON type, and four places called them unguarded

`JsonElement.TryGetInt32` returns `false` only for a number that will not fit in an `Int32`. Given a
string, a bool, an array or an object it throws `InvalidOperationException`. So:

- `"period_duration": "60"` threw out of `ValidateRoutineRequest`, inside the controller's catch-all,
  and came back as **`500` "Internal server error occurred while generating the schedule"** instead
  of the `400` "Period duration must be a positive number" that was sitting right there.
- Same for `periods_per_subject`.
- In `PUT /teacher/update`, `teacherIdEl.TryGetInt64`, `salutationEl.GetString()` and
  `nameEl.GetString()` all ran **outside** that method's `try`, so a wrongly-typed field left as an
  unhandled exception.

**Fixed** with `RoutineRequestValidator.TryReadPositiveInt`, which checks `ValueKind` first, and
explicit `ValueKind` checks on every raw read in `TeachersController`. `subjects_array` and
`availability_json` are now deserialized and validated rather than re-serialized blind.

### 6. A section with a null or empty subject list

`{"periods": {"1A": null}}` threw `NullReferenceException` from `Values.Max(s => s.Count)` — a `500`.

`{"periods": {"1A": []}}` was worse because it succeeded: zero assignments means the search reaches
its terminal check immediately, `ValidateRoutine` finds nothing wrong with a grid it was asked
nothing about, and **an entirely blank timetable was saved as a routine** and served as a page of
dashes.

**Fixed:** both refused, naming the section.

### 7. A subject listed twice in one section could never be scheduled

The engine groups assignments by (section, subject), so a code repeated in one section asks for
`periods_per_subject` periods twice under a single identity — while `ValidateRoutine` initialises
that subject's counter twice and compares the single accumulated total against
`periods_per_subject`. Every complete schedule the search builds is therefore rejected, and the
search becomes an exhaustive walk of a space with no solution. Another silent slow `500`.

**Fixed:** refused up front, naming the code. The same subject in two *different* sections is
ordinary and is explicitly tested as still allowed.

### 8. The (section, subject) lock key collided

Both locking dictionaries were keyed on `$"{section}-{subject}"`, and `-` is legal in both halves.
Section `1A-B` teaching `C` and section `1A` teaching `B-C` produce the same key, so a lock taken on
one was read back for the other and the engine constrained a subject it had never placed.

**Fixed** with a `(string Section, string Subject)` tuple key — no concatenation, so nothing to
collide. Contrived on the current data, trivially cheap to remove, and the sort of thing that is
impossible to debug when it does happen.

### 9. A malformed stored availability window broke every generation, unattributably

`TimeUtils.TimeToMinutes` split on `:` and indexed `parts[1]` unconditionally. Availability comes out
of a JSON column that nothing validated, so a window of `"9"` instead of `"09:00"` was stored
happily by `POST /teacher/add` — and then **every** later `/generate`, for any section, whether or
not that teacher was a candidate, failed with `IndexOutOfRangeException` reported as an internal
error naming nothing. The only way to find the row was to guess.

**Fixed** at three levels:

- `TimeUtils.TryTimeToMinutes` for callers that can handle a refusal; `TimeToMinutes` throws
  `FormatException` quoting the offending value.
- `TeacherPayloadValidator` rejects malformed windows, windows that end no later than they start, and
  day names that are not days, on both `add` and `update` — so the request that introduces bad data
  is the request that fails.
- `RoutinePlannerService` validates every loaded teacher before the search starts and throws
  `InvalidTeacherDataException` naming the teacher; the controller returns `400` with that message.
  This is what handles rows already in the database.

### 10. The routine page was composed with `Regex.Replace`, and the replacement held user data

```csharp
html = ClosingHtmlTag().Replace(html, routineScript + "</html>", 1);
```

`Regex.Replace` interprets `$` sequences in the **replacement**, and `routineScript` is the
serialized timetable. Section names come straight off the `/generate` body; teacher names come out of
the database.

`$_` is .NET's substitution for the entire input string. A section named `$_` embedded the whole HTML
document inside the script literal — and the real template contains a `</script>` tag, so that closed
the block early and spilled the rest of the document into the page as markup. Against the original,
with the shipped template: `<!DOCTYPE html>` appears **twice** and `</script>` **three times**. A
name containing `$$` silently arrived in the browser as a single `$`.

**Fixed** by extracting `Services/RoutinePageRenderer` — testable without a filesystem or an HTTP
context — and inserting with `IndexOf` and a slice. Equivalent to the single `Regex.Replace` for
every input except the ones the regex engine would have reinterpreted.

Two things the same test file now records as **not** bugs, so nobody "fixes" them:

- A name containing `</script>` genuinely cannot close the block. `System.Text.Json`'s default
  encoder escapes `<`, `>` and `&`, so it arrives as `</script>`. The renderer relies on
  that rather than escaping twice, and there is a test saying so.
- A template with no `</html>` now still gets the routine. It used to get nothing at all, silently —
  `Regex.Replace` found no match and returned the page unchanged, so the script that draws the
  timetable had no data.

### 11. `GenerateTimeSlots` with a non-positive duration never returned

`currentTime` never advanced, so the loop appended zero-length periods until memory ran out. The
validator rejected `<= 0` before the engine saw it, so this was unreachable through the API — but the
validator now calls `GenerateTimeSlots` itself, and an unrecoverable hang is a poor failure mode for
a public static method. It throws `ArgumentOutOfRangeException`. Against the original the probe was
**still running after three seconds**.

### 12. Efficiency: workload was recomputed inside the sort comparison

`candidates.Sort((a, b) => GetTeacherCurrentWorkload(a, routine).CompareTo(GetTeacherCurrentWorkload(b, routine)))`
— and `GetTeacherCurrentWorkload` is a full walk of the grid. That is O(n log n) grid walks per
candidate list, at every node of the backtracking search. Measured once per candidate now, and the
pairs sorted on the measurement. Not a wrong answer; it was in the hottest path there is.

## The review's own three findings

**1. Committed SQLite databases.** Confirmed: real data about real people. Untracked, gitignored,
replaced with a reviewable JSON seed plus a `--seed` command that refuses to touch a populated table
without `--force`. See `PERSONAL-DATA-TODO.md`; the history decision is the author's.

**2. "Confirm the runtime writes to a copy, not the tracked files."** It did not. `dotnet run` uses
the project directory as its content root, so the application read and wrote
`App_Data/campus.sqlite3` itself — the tracked file. Directly observed during this pass: running
`--seed` against it reported "the teachers table already has 10 row(s)", which are the ten real
people. The csproj's elaborate seed-if-missing machinery protected the *output* copy while the
documented development workflow bypassed it entirely. Resolved by finding 1 — nothing mutable is
tracked now, so ordinary use cannot dirty the tree.

**3. "Confirm unsatisfiable constraints are reported explicitly."** They were not, and this was the
most productive question in the review. What actually happened was that arithmetically-impossible
requests were *accepted* and searched (findings 2, 3, 6, 7), unbounded (finding 4), and then reported
with one generic message. The fix is in two parts: refuse what can be refused, with a message that
names the rule, and bound what cannot.

Half of the finding is deliberately **not** done. `GenerationLogger` output still goes only to
`logs/`, and the response still carries one sentence rather than a list of conflicting constraints.
Naming which constraints conflicted means extracting a reason from a failed exhaustive search, which
is a real feature and a UI change — the page has nowhere to put it. The cases a user can act on are
now caught before the search, which is where the value was.

**4. Determinism.** Not made deterministic, which would be the wrong fix: the randomness is what lets
parallel attempts cover different parts of the space. Instead the seed is surfaced —
`RoutinePlanner:Seed` fixes it, and every run writes the seed it used into its log, so a good
timetable can be reproduced. Tested both ways: same seed, same timetable; eight different seeds, more
than one distinct result.

**5. No `LICENSE`.** Not added. Choosing a licence is the owner's.

## Deliberately not done

- **Authentication.** Still none. Adding a login changes how the application is used, which is out of
  scope for a business-logic pass, and the review itself frames it as a decision ("decide which it
  is"). The README now says plainly, near the top, that it must not be exposed, and sketches the
  smallest fix. `PERSONAL-DATA-TODO.md` repeats it for the author.
- **`GetAllTeachers` and friends return `ex.Message` in their 500 bodies.** That leaks internal
  detail to a client. Left alone: it is three call sites, the messages are plainly useful for
  debugging a local tool, and changing them is changing error copy for no behavioural gain. Worth
  doing if this ever goes behind a login.
- **Naming which constraints conflicted** on failure — see finding 3 above.
- **A unique constraint on teacher names.** Deliberately not added. Two people can share a name;
  that is the world, not a data error. The engine now handles it, which is the right level to fix it
  at. Note that the timetable still *displays* only the name, so a human reading it cannot tell two
  namesakes apart — that is a UI question, and the data now supports answering it if anyone wants to.
- **Decomposing `Backtrack` further.** It is long but it is one algorithm, and the pieces it would
  split into are already the classes around it. Left as the review asked.

## Notes for whoever is next

- `dotnet test` — 231 tests. The originals' 26 are all still there and all still pass, unmodified.
- **The one dependency added** is `Microsoft.AspNetCore.Mvc.Testing` 8.0.8, to the test project only.
  It is what makes `GenerateEndpointTests` and `TeacherEndpointTests` possible, and asserting on what
  a client actually receives turned out to matter here more than usual, because the gap between "the
  server knows why this failed" and "the user is told why" was itself several of the defects.
  `Program.cs` gained `public partial class Program { }` at the bottom so the factory can reach it.
- **Use `TempAppDirectory` or `RoutinePlannerTestApp`**, never the real `App_Data/`. Both give a test
  its own content root in the temp directory. The second copies the real `wwwroot` and
  `courses.json` in, so page rendering is tested against the template that ships.
- **`GeneratedRoutineAssertions` is the thing to reach for** when changing the engine. It checks every
  rule independently of the engine's own completeness check, and
  `The_invariant_checker_rejects_a_timetable_that_breaks_a_rule` proves it can fail — worth keeping,
  because a vacuous invariant checker is worse than none.
- **`Services/Scheduling/` has no I/O in it** and should stay that way; it is why nearly all of this
  is testable without a host.
- If you add a validation rule, put it in `RoutineRequestValidator` and give it a message that says
  what the rule *is*. The frontend renders `error` verbatim, so those strings are the interface, and
  "Schedule not feasible: need 36 periods but only 30 slots available" was true and useless when the
  real problem was that six periods cannot fit into five days.
- The comments naming `.js` files throughout are accurate history — this is a port — but **the
  original is not in this archive**, so there is nothing to diff against. Do not go looking.

## Licence, 2026-10-02

The author chose one licence for all of their projects: free for personal,
non-commercial use, modifiable for the user's own use, and not to be
distributed, original or modified. No standard licence says that, so `LICENSE`
is a short custom text, the same in every project apart from the year and what
it excludes. The README's licence section says the same. Anything above that
calls the licence an open question, or calls the project MIT, is superseded.

## CI, 2026-10-02

`.github/workflows/ci.yml` runs `dotnet test` on .NET 8, `ubuntu-latest`.
Nothing has been pushed, so it has not run on GitHub yet. Every job was run
before committing, from a fresh clone of this branch, the way a runner would
run it: Linux jobs in a throwaway Ubuntu 24.04 (the image `ubuntu-latest`
uses) with the same tool versions. 231.
