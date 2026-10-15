# maash

**maash** is a language-agnostic shell tool and preprocessor designed for
intermingling other languages into your bash shell scripts.

## Naming

**M**acro **A**t-**A**t **Sh**ell (or **M**aash **A**int **A** **S**hell)

## Purpose

This is a convenience tool for invoking better scripting/programming languages
from within bash. If a user has a bash script and wants a minimal incremental
drop-in use of another language they know well, this provides a convenient way
to mix that in. The goal of maash is that it will be 100% just bash unless a
maash operator is used.

It uses `@@` prefixes to invoke other programming languages within bash.  It is
intended to be language agnostic about what those other languages are.

### Preprocessing

Note that while maash has a preprocessor, it is *not* intended to output the
final string values.  The maash preprocessing converts maash operations into
properly escaped invocations of `maash eval`.  While maash invocations are
parsed as early as possible, their undlerlying interpreter is invoked within
their bash commands.  The preprocessor is syntactic sugar over `maash eval`.

## Interfaces into maash

The user will have 3 main ways to use it in scripting:

1) The maash program.  A binary with many subcommands that can execute
   shell-scripts with a maash shebang.

2) The maash hotkey.  A standard hotkey ctrl-alt-M that takes the current input
   bash text buffer and runs the mash preprocessor on it, set using the normal
   "bind" operator in bash.

3) The maash function "@@" - this is a convenience tool for using maash eval
   without using the maash preprocessor.

## The maash program

When called with no subcommands, the maash program is for running script files.
Interactive support is low-priority.  If included, that will be minimum viable
product: Read from stdin, process through the maash preprocessor, then send the
output to an active bash session.  If used as a shebang in a script, the opening
lines of the script will be handled as maactl subcommands without the leading
"maactl" up to a "^---$" break.

### maash subcommands

- `maash ctl [subcommands]` AKA **maactl** (an alias for this subcommand used
  below).  It's a meta-operator full of subcommands to control the maash system.
- `maash preprocess [-f|--file file-path]` Preprocesses the given file and
  returns it as text to stdout without executing it in bash.  If run with no
  argument at all it preprocesses stdin.  If run with an argument and not the -f
  argument, it preprocesses the given text body of the argument.
- `maash eval [maash-operator-suffix] [[argv]]` invokes a maash operator (given
  using the module infix and operator suffix) from the current `maactl` and
  returns the result to stdout.
- `maash <something, name pending> [string]` a form of the preprocessor
  specifically designed for the maash hotkey, where maash operations are fully
  evaluated and the resulting bash is provided.
- `maash init` intended to be run in the caller scope ie in their .bashrc, run
  with `eval "$(maash init)"`, handles creating the maactl alias and the @@
  function and any other in-bash customization needed.
- `maash help` [module-alias|module-name|maash-operator|maash-operator-suffix] -
  get help about a module or operator.
  - arg -f [file-path] --file to get help on a module by module file.

## maash modules

A maash module registers one or more maash operators, all of which start with @@,
then an infix (which is overridable when a module is loaded), then a suffix
(which is per-operator).  When a module is loaded, an interpreter for the
language is spawned and its state persists until the module is removed.  This
allows maash operators to use variables across the script.  maash operators must
also be able to share the environment variables with the parent bash
context/scope as seamlessly as possible, defining environment variables within
the maash operators script and having them appear in the encapsulating bash, and
vice versa.  These will not be the same as standard native variables, but will
be stored in a language-appropriate collection (for example the `$env:` variable
scope in pwsh).

Because a maash module must persist and must do substantial transformations on
the data going in/out, it can be more than just a .json definition.  A maash
module will likely need an underyling dll or npm package or other form of
assembly in the target language to interact with maash.

## maactl

maactl can be used both interactively *and* in script.  However, in script the
maactl subcommands should be in the header of the file (before the `---`),
without the maactl/maash ctl prefix.  Using maactl subcommands within the body
of the script will mean they're only being used for the context of the @@/`maash
eval` function, not the preprocessor.  A warning should be written to stderr
when this happens.

This distinction (that maactl subcommands can be used in file headers) are why
they have their own subcommand.

### maactl subcommands

- `maactl use [module-name]` - uses a maash module from module search dir
  (probably `/usr/local/share/maash`). Modules are loaded by name, which is
  filename/path relative to OS-defined module search dir.  Using a maash module
  activates an interpreter with an ongoing context and registers its maash
  operators to send things to that interpreter.  The argument is the interpreter
  name which is the extensionless path relative to the module search path.  So a
  module `share/maash/custom/fsharp3` would be loaded as `maactl use
  custom/fsharp3`. The "custom/" is part of module name/default-alias.
  Standard, common modules would be located directly in the module search dir
  `/usr/local/share/maash`, eg `/usr/local/share/maash/csharp.json` would just
  be imported as `csharp`.
  - arg -f [file-path] --file path to a module file
  - arg -i [jsons-string] --inline declare a module json right here
  - arg -a [alias-string] --alias sets the module-alias, defaults to use-name
    *or* filename if loaded from file.  Error on collision.
  - arg -x [infix-char] --infix adds a custom infix to the maash-operator to
    avoid collisions.
  - arg -p --primary makes it primary -If have only 1 maactl module loaded it is
    inferred primary -If you load a 2nd module and your existing module is
    inferred primary
      - WARN that primary is ambiguous
      - Remove primary flag
  - Any collisions on module-aliases or fully-resolved maash-operators are errors
    *except* that the primary module can be shadowed.  If a primary operator is
    shadowed, the user can still access it through the infix.
    - eg: 2 modules, c (infix c) and c# (infix #), where c is primary, and the c
      module registers # suffix as a operator for preprocessor directives, that
      would collide with C#'s primary operator which is registered at @@#, but
      the c preprocessor operator could be accessed through @@c# so it's okay if
      the @@c# is clobbered by dotnet (should WARN through stderr though unless
      some kind of allowclobber param is provided in the `use`).
- `maactl move [module-alias]` - accepts the `use` params for alias, infix,
  primary so an existing module import can be moved to a new name.
  - while primary is a switch-param in `use`, here it accepts a boolean
    parameter.
- `maactl load [file-path]` - is an alias of `maactl use -f [file-path]`
  - Same params as "use".
- `maactl remove [module-alias]` - removes something imported by maactl use
- `maactl help` [module-alias|module-name] - same as maash help.
- `maactl clean` - remove all imported modules

`/usr/local/share/maash` contains your installed maash plugins.  These are a
universal namespace so users should avoid customizing them.  For userland
plugins they go in `~/.local/share/maash`.

### maash operators

In maash, all operators are prefixed with `@@`.  The maash module can register
as many suffixes to @@ as it likes.  The `@` suffix is special, it's the
"primary operator" which also used when no suffix is provided.  When maash sees
a @@ in the line (or in the script, if run in script mode) it checks for any
maash operators greedily (so longest-operator-wins).  Maash operators and their
operands are then sent onto their interpreter of the module's choice. It is the
module's responsibility to provide not just output, but also respond with the
length of the parsed operand so that maash can know what range of the bash
string to replace.  If the module cannot parse and determine the length of the
operand, maash will error unless run in bracketed mode, in which the maash
operator parameter ends with `]@@`.

Maash operators will generally accept exactly one operand in script-file mode,
although the `maash eval` and `maash preprocess` subcommands will accept an
array of arguments and concatenate them together as a single operand to the
underlying module.

So, for a C#-based maash-operator with the infix `#`

```
@@#"hello, world".ToUpper()
```
Or in bracketed mode
```
@@#["hello, world".ToUpper()]@@
```

A module can register multiple operators.  By convention, braced mode can be
provided by a module `@@#{var myVar = "hello, world"; return myvar;}` - it might
be difficult for maash itself to detect that, but a smart interpreter can offer
that.  A module can define whatever subdialects they like within their operators,
that's just part of the existing operator.

#### The Primary Module

The "primary" module gets to use the unprefixed @@.  Every other module must
live on their infix, with their individual operator suffixes after that infix.
So if C# defined @@ for expressions that return and @@~ for things that don't,
and it was loaded as non-primary with the # infix, then the operators would
become @@# and @@#~.

#### Alphanumeric infixes

Because alphanumeric infixes are prone to confusion, if in infix is alphanumeric
the primary operator does not get to use the empty suffix.  So if python is
loaded on infix "py" and it's not primary, then its primary operator goes to @@
to @@py@.  This behavior can be suppressed with an argument in `use` when the
module is loaded.

#### Optional Whitespace

The maash operator may be followed by whitespace before its operand.  By
convention the maash operators will *not* be invoked with space, but there are
places where it may be needed for clarity.

#### Maash operator operand

The maash operator operand can span multiple lines.

#### Maash operator api

A maash operator is effectively a command-line program - it can write to stdout
and stderr and ends with an exit-code.  The module will be expected to handle
making this convenient - in C#, a basic operator would expect an expression that
returns an `IEnumerable<strings>` that the module would write to stdout, after
which exit-code would be set to 0.  Any exceptions would be written to stderr
and result in non-zero exit code.

## The @@ function (implemented as a normal bash function)

A simple executable wrapper for `maash eval`, if somebody doesn't want to use
the maash plugin they can use `@@ # 'runMyFunction()'`

## C# maash module operator ideas
- @@ - eval and return result strings to stdout
- @@& - eval void.  If the expression returns `IEnumerable<string>` they go to
  stderr but without error highlights.
- @@? - evals a boolean expression and sets an exit-code of 1 for false, 0 for
  true.
- @@$ - eval and execute the result in bash.
- blocks?(see blocks enhancement)
  - @@if{ @@}
  - @@using{ @@}
  - @@foreach{ @@}

## Escape characters

Tedious but necessary. Nothing special: `\` at the start of an operator or
terminator to have. Similarly, need to support the posix-standard `--`
thing to handle collisions between filenames and subcommands (does
`system.commandline` do that out-of-the-box?).

## Implementation

Since C# is my main language, a modern dotnet 10 statically-compiled C# "Native
AOT" exe seems the way to go. Use system.commandline if possible.

## Future Enhancements

### maash bake
A subcommand to preprocess the given file *and* execute the underlying maash
operators.  Outputs the source file content with the maash operators and
operands replaced by their corresponding resulting text.

### More languages
pwsh `>_` and node `j.`(?) are obvious choices.

### Blocks?

As a future enhancement, there will need to be some facility for nesting bash
within maash, possibly repeatedly, for things like the C# razor template
@foreach functionality.

Block operators that take bash content will be tricky.  Probably the block bash
content would get passed to the operator as N parameters, each separator would
have its own token (like `@@} else {` and `@@}`). And the maash preprocessor
would need to also work on the output of that inner block.

When to escape things would also be tricky, as well as when to recurse, since
if/else would be expected to contain and yield unescaped bash code that will be
re-evaluated by the maash preprocessor.

This also raises complex questions about nested bracketed invocations, the
parser can't just look for the first incidence of `]]@@`.

### Alternate shells?

Support for zsh?
