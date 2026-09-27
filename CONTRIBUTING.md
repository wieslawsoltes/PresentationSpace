# Contributing

Use the .NET SDK and Uno version pinned in `global.json`. Keep changes scoped to the owning module and preserve the dependency direction documented in `docs/architecture.md`.

Before submitting a change, run the headless tests and compile the desktop and browser targets. Add regression tests for editing, geometry or file-format changes; include malformed-input cases for parser changes. Browser behavior should be exercised through actual keyboard/pointer input, not direct test-only state mutation.

Document unsupported cases and conversion losses. Do not claim PowerPoint parity from a schema test alone. Do not bundle proprietary fonts, Microsoft logos, commercial controls or restrictive-license dependencies. Prefer permissively licensed dependencies with a clear maintenance benefit.

Use descriptive commits such as `feat(core): ...`, `fix(viewport): ...`, or `test(formats): ...`. Keep generated binaries, build output and developer credentials out of the repository. All contributions are under the MIT license.
