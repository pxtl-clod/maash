# AGENTS.md

See [CONTRIB.md](CONTRIB.md) for coding style guide. Read the CONTRIB.md fully
before making changes.

See [TODO.md](TODO.md) for the TODO list.  Delete items from the TODO list when
complete.  Each major TODO heading should be implemented as its own PR with its
own branch from main.

# Dev Environment

Codebase is using dotnet 10.0.  Use `dotnet build` to compile, and `dotnet test`
to run unit test suite.

# Testing

Unit tests are in `tests`.

# Refactoring

Do not attempt to refactor or otherwise clean-up the code unless specifically
instructed to do so. Do not remove comments unless they are untrue.

# Scratch space

Create any needed working files such as experiments within the `temp` dir, which
is gitignored.  Create personal notes for future reference in `MEMORY.md`, which
is also gitignored.
