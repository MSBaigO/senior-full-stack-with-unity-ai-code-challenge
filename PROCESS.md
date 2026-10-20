# Process

## AI tools used

Claude Code (desktop app, Sonnet 5.5) for everything: reading the scoped files, planning, TDD, running tests, the browser check. Unity tests ran through the Unity CLI (`unity test`, EditMode and PlayMode, editor 2022.3.62f1); API tests through `dotnet test`; the dashboard was checked in the built-in browser. `interview.log` is the hook log from the session.

## Where AI output was wrong or misleading

- **The root cause of the ticket was AI-style "plausible" code in the starter.** `SketchGeometry.TotalLengthFeet` carried a confident comment ("crews sketch on the floor plane... measure the run the way it reads on the plan sheet") and dropped `y`. A pure vertical drop measured 0 ft and failed the 0.5 ft minimum. It also under-reported every run with a rise in `clientLengthFeet`. The API copy was right.
- **Mine: a false "committed".** My first Task 1 commit failed on PowerShell here-string quoting, but I reported it as committed. I caught it from the `git log` output, re-committed from bash, and corrected the report. Lesson: check `git log` before claiming a commit.
- **Mine: test typo.** An anonymous array mixing `int` and `double` did not compile; fixed before the red run so the failures were about the missing endpoint.
- **Unity specifics that bit:**
  - `PlayerPrefs` persistence leaks between tests, so `PublishService.Queue` got a public setter and tests use their own key.
  - A start-up drain would race the tests' own drain, hence `DrainOnStart` (tests turn it off).
  - `JsonUtility.FromJson` throws `ArgumentException` on non-JSON bodies (e.g. an HTML 502 page), so `ApiErrorDto.Describe` catches it.
  - Unity regenerates `.meta` files and `ProjectSettings/*` on first open; I did not commit them.

## How I validated what I kept

- Every change followed red then green. I ran each new test red first, with stubs where needed so the failure was an assertion and not a compile error.
- Task 4 used shared parity vectors (12, 13 and 42 ft) in the API and the Unity tests. The API tests passed immediately, which confirmed the API is the source of truth.
- Task 1 has API integration tests through `WebApplicationFactory`. A live run confirmed 201, then 200, then 409 and 400, and the dashboard showed the new sketch without a restart.
- Last results: API 26/26, Unity EditMode 27/27, Unity PlayMode 24/24. Each task was committed on its own.
- The dashboard confirmed the ticket's "footage light" story: all 26 mismatched seeded sketches have a rise, and the client value is lower in every one. The client total is 9,807 ft against 10,093 ft on the server (about 2.8% light). The shop's own "Total footage" was already server-side and correct.

## Decisions: publishing, duplicates, errors

| Situation | Behaviour |
|---|---|
| New sketch | `201`, server sets `Status=Published`, `PublishedAtUtc`, `Revision=1`, history. The client cannot set status. `capturedAtUtc` is required. |
| Same id, same content | `200`, no state change. Unity treats it as `Succeeded` with `IsDuplicate` ("Already published (duplicate)"). |
| Same id, different content | `409` with a clear message. Content includes `clientLengthFeet`; `clientVersion` and `capturedAtUtc` are ignored so an app upgrade does not break a retry. |
| Rejected sketch republished after rework | `201`, `Revision++`, history `Rejected→Published`. |
| Invalid | `400` with `{error, failures[{field,message}]}`. |
| No signal or timeout | Persisted to the queue, `QueuedOffline`, the drain stops at the first network failure. |
| 5xx | Persisted, `QueuedForRetry`, up to 3 retries, then held. |
| 4xx other than 409 | `RejectedByServer` with the server's reason, persisted, up to 3 retries, then held. |
| 409 | Persisted and held immediately, never auto-resent. |
| Held | Kept on the tablet, skipped by the drain. Republishing under the same id replaces it. |

The foreman tells outcomes apart through `PublishResult` (`Outcome`, `StatusCode`, `Message`, `IsDuplicate`). The queue drains at start-up and after any successful publish.

## Throughput (not built)

I ran out of time, so only the definition and plan exist.

- **Definition:** per UTC day over the last N days (default 14), measured in feet using the server's 3D `LengthFeet`.
  - `newFeet`: first publishes (`Revision == 1`) by `PublishedAtUtc`.
  - `reworkFeet`: republishes after rework, counted separately and never in `newFeet`.
  - `reviewedFeet`: sketches with an `Approved` or `Rejected` history entry that day.
  - `backlogDeltaFeet = newFeet + reworkFeet − reviewedFeet` (positive means the shop is behind).
  - Plus a summary median hours to review.
- **Assumptions:** UTC days; unreviewed sketches count as backlog and are excluded from the median (making it optimistic); reviewed feet attributed to the day of the review event; seed timestamps are rebased to "now", so seeded history clusters in the last ~3 weeks. I did not verify that every seeded sketch has review entries in its history.
- **Planned shape:** `GET /api/dashboard/throughput?days=14` returning one row per day (zero days included) plus a Vue table replacing the "Not built yet" panel.

## What I cut, and why

Throughput (API and Vue) and its tests: out of time. Per-crew breakdown, rework rate and charts: out of scope for this slice.

## Known gaps and what I'd do next

1. Build throughput as planned, test first.
2. Drive the offline path with a real timeout and a live API from a running scene; today it is tested only against the stub transport.
3. Give held sketches a way to be edited or discarded in the HUD, and drain when signal returns, not just at start-up or after a publish.
4. A permanently invalid 400 sketch currently burns its 3 retries against the server before it is held; consider holding it at once with a client-side check.
5. The in-memory check-then-write is serialised by one lock; a real store needs a conditional insert or a unique id.
6. Make the queue's id comparison case-insensitive to match the server.
7. Decide whether to commit Unity's generated `.meta` and `ProjectSettings` files, and the web app's `package-lock.json`.
