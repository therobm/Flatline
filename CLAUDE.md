# Flatline

Self-hosted bug tracker. .NET 8 with TcpListener, SQLite, vanilla HTML/JS/CSS frontend.

## Standing Orders

- After every change, update this file to reflect new rules learned from corrections. Do not add feature descriptions, implementation details, or project state.
- Do not add features, libraries, or files that were not requested.
- Do not refactor working code unless asked.
- When fixing a bug, fix the bug — do not rewrite the surrounding code.
- Ask before deleting or renaming anything that already exists.
- At the start of each session, pull main and create a fresh branch named after the first task.
- All work within a session stays on that branch. Commit as you go.
- When I say it's good, push the branch and create a pull request to main. Do not merge.

## Tech Constraints

- SQLite via Microsoft.Data.Sqlite. No Entity Framework, no ORM, no query builders, no LINQ-to-SQL.
- All SQL is explicit, hand-written, parameterized.
- API returns JSON. Frontend is plain HTML/JS/CSS served from wwwroot/. No React, no Blazor, no SPA framework, no JS build tools.
- Single-project deployment: one executable, one database file. No Docker, no cloud dependencies, no external services.
- Cookie-based auth with bcrypt password hashing. No OAuth, no JWT.
- Schema managed via versioned migration scripts (plain SQL, applied on startup).
- Statuses, priorities, and any other enumerated values are defined once in the backend and served to the frontend via API. No hardcoded enum strings in the JS.
- No dependency injection containers. No service registration. No framework-managed object lifetimes. Create objects explicitly and pass dependencies through constructors or method parameters.
- No interfaces implemented solely for framework discovery. If the framework can't call it through a direct, traceable code path, don't use that framework feature.
- No middleware pipelines, request filters, or processing layers that run on every request unless explicitly added. Every request hits the handler directly. If it's not needed for that request, it doesn't run.
- Bugs belong to a project. Projects have versions. Bugs track found-in and fixed-in versions.

## C# Code Style

Types and Declarations
- Explicit types everywhere. No var.
- No nullable types of any kind. No ? on reference types or value types. Use sentinel values or separate flags to indicate absence. Turn off nullable in the csproj.
- No implicit usings. No global usings. No using aliases. Turn off implicit usings in the csproj.
- Use enums for fixed sets of values. No magic strings. Enums are prefixed with e, for example enum eBugStatus { Open, Closed }.

Naming
- Member fields use m_ prefix. No _ prefix, no bare names for instance fields.
- Verbose, descriptive naming. No abbreviations. No acronyms without context.
- No single-character variables except i, j, k for loop counters.
- One class per file. File name matches class name. Exception: small request/response data classes used by a single route file stay in that route file, enums stay in their primary class files, etc..
- No this. prefix unless resolving ambiguity.

Structure and Syntax
- No lambdas. No => expression-bodied members. No computed properties.
- No inline ternaries embedded in property definitions or assignments.
- No shorthand syntax of any kind. No string.Empty, use "".
- Braces on all blocks, even single-line if/else.
- i++ not ++i.
- Add/Remove over Push/Pop.
- Prefer early return over deep nesting.
- No regions.
- No default parameter values. Use overloads if needed.
- Methods over properties when there is any logic involved.
- Public fields over properties for plain data classes. No { get; set; } ceremony on data bags.
- Never use while loops unless you specifically require an infinite loop. Use for loops with defined limits.
- All for loops must have an explicit terminal condition. No Count recalculated every iteration if avoidable.

Patterns to Avoid
- No wrapper methods that just pass through to a single framework call. Call the framework method directly at the call site.
- No builder/factory methods for simple object construction. Just set the fields directly.
- No static utility classes that could be plain methods on the type they operate on.
- No empty catch blocks. If you catch, handle it or rethrow.

Data and Serialization
- All SQL is explicit, hand-written, parameterized. No string interpolation in SQL.
- No name transformations in serialization. Field names match wire names exactly. No JsonPropertyName, no naming policies.

## JS Code Style

- No arrow functions. Named `function` declarations only.
- `let` and `const` only, no `var`.
- No ternaries. Use `if`/`else`.
- No frameworks, no modules, no import/export. Single `app.js` file with plain functions.
- Descriptive function and variable names. No abbreviations.
- All DOM event handlers are named functions attached explicitly — no inline `onclick` in HTML.

