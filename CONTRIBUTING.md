# Contributing

Issue reports and pull requests are welcome.

## Before opening a pull request

- Explain the user-visible behavior being changed.
- Keep changes focused and update the README when supported behavior changes.
- Do not commit build output, the embedded FFmpeg executable, personal paths, or input media.
- Confirm that the project builds on Windows with `.\build-single-exe.ps1` when the change affects the release artifact.
- Run `git diff --check` before submitting.

## Bug reports

Include the Windows version, application version, input naming pattern, selected output format, and the relevant error message. Do not attach private media unless it is necessary and safe to share.
