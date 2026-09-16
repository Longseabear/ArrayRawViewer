# Repeated Watch X/Y: ownership and release verification

## Change

The viewer previously discarded `Breakpoints.Add`'s return value and tried to
rediscover its breakpoint from the global list using address text. If that lookup
never succeeded, Clear and Next repeatedly scanned the same list without ever
acquiring the object needed for deletion. A one-shot hit also reported removal
even when cleanup failed.

The adapter now retains the unique new breakpoint returned by Add. The SDK API
returns a Breakpoints collection ([Microsoft API reference](https://learn.microsoft.com/en-us/dotnet/api/envdte.breakpoints.add?view=visualstudiosdk-2022)).
Both enumerable and Count/Item automation collections are supported. Address-based
resolution remains a conservative fallback for engines that do not return a usable
entry immediately. A pre-existing or ambiguous entry is never adopted.

Deletion is a two-stage operation:

1. Request Delete on the owned object, retaining the registration.
2. Confirm absence using a complete breakpoint snapshot before allowing a new Watch.

An acknowledged but unconfirmed deletion is not sent repeatedly. A failed Delete
may be retried. Enumeration failures and new same-address identities remain
explicitly unresolved: the viewer never deletes an unknown replacement to get
past an error. Clear processes all retired registrations. One-shot status reports
pending cleanup truthfully. Session end retains unresolved ownership; queued work
outside break mode is cancelled without resuming the debugger.

## Automated verification

- OptionsPagePreview `--verify-breakpoints`: synchronous/asynchronous removal,
  retry after failed deletion, returned object/collection binding, delayed
  publication, Count/Item-only collections, same-address replacement protection,
  incomplete/unreadable snapshots and 50 alternating X/Y address cycles.
- The 50-cycle test keeps a user source breakpoint and verifies it is never deleted.
- ViewerWorkspacePreview `--verify`: one-shot cleanup failure messages, retained
  ownership across session end, cancelled retries outside break mode, existing
  cursor/centering, focus, tab and layout regressions.

These tests use synthetic automation objects and a standalone WPF host. They are
not proof of the native engine's timing in every Visual Studio release.

## Manual native Visual Studio check

1. Build/install the updated VSIX, open the native sample and stop before writing
   the selected output buffer. Leave an ordinary user source breakpoint present.
2. Watch a future output pixel, then alternate Watch Next X / Watch Next Y at least
   50 times while writes remain ahead of the program counter. The selected pixel
   and view should follow the watch target without accumulating viewer breakpoints.
3. Clear after a hit and while cleanup is pending. A pending registration must not
   be described as removed. New Watch is allowed only after confirmed cleanup.
4. Interrupt at a user breakpoint before the watched write, then Clear or select a
   different target. User breakpoints must remain untouched.
5. Stop/restart debugging while cleanup is pending. Confirm remaining viewer-owned
   entries are retried, not forgotten or mistaken for new user breakpoints.
6. If release remains unresolved, copy the full status including requested address,
   byte count and snapshot/identity failure. Do not delete all user breakpoints.

This change does not automatically claim ownership of unknown data breakpoints
left behind by an older extension instance.
