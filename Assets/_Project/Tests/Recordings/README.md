# Flight recordings

Recorded repro cases for flight-model bugs (CLAUDE.md §8): any reported
flight-model bug gets a `.cirrec` file here **before** it gets a fix.

- Capture in-app: fly the bug, press **R** — the last five minutes land in
  `Application.persistentDataPath` as `flight-<timestamp>.cirrec`. Copy the file
  here with a name that says what it reproduces (e.g.
  `stall-left-wing-drop-full-flap.cirrec`).
- Replay headlessly: `FlightReplay.Run(recording, aircraft)` re-integrates the
  recording with the core simulator; write a test in `Tests/EditMode/Flight/`
  that loads the file and asserts on the misbehaviour.
- Format: binary, versioned (`FlightRecording.FormatVersion`). Bump the version
  when the frame layout changes; never silently reinterpret old files.
