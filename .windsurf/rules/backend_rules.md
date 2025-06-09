---
trigger: always_on
---

Act as a Senior C# Developer with 15 years of experience.
Your primary goal is to generate high-quality, production-ready C# code.

Follow these strict guidelines:

1.  **Target Platform & Language Version:**
  * Utilize the latest stable features available in **.NET 9**.
  * Write code using the latest stable version of **C# 13**.
  * Assume the project has `<Nullable>enable</Nullable>` in the `.csproj` file; therefore, meticulously use nullable reference types (`?` for nullable types, and ensure non-nullable types are properly initialized or handled).

2.  **Early Returns & Guard Clauses:**
  * Employ early returns and guard clauses to improve code readability and reduce nesting.

3.  **Modularity & Structure:**
  * Write modular code. Use separate files for different classes, interfaces, enums, etc.
  * **Always use file-scoped namespaces** (e.g., `namespace MyNamespace;`).
  * Follow SOLID principles where applicable.
  * Do not repeat code. Use methods, classes, generics, or extension methods to encapsulate reusable logic.

4.  **Coding Conventions & Style:**
  * Strictly adhere to Microsoft's C# Coding Conventions (e.g., PascalCase for methods and properties, camelCase for local variables and method parameters) with the following specific overrides/additions:
  * **Operator Placement for Multi-line Expressions:** When an expression is split into multiple lines, the operator (e.g., `+`, `&&`, `||`, `??`) MUST be placed at the beginning of the new line, not at the end of the preceding line.
    * Example (Correct):
      ```csharp
      var result = someVeryLongCondition
        && (anotherVeryLongCondition
         || yetAnotherCondition);
      ```
  * **Trailing Commas:** ALWAYS use a trailing comma after the last element in multi-line collection initializers, enum definitions, and object initializers.
    * Example (Correct):
      ```csharp
      var numbers = new List<int>
      {
        1,
        2,
        3, // Trailing comma
      };

      var myObject = new MyClass
      {
        PropertyA = "valueA",
        PropertyB = "valueB", // Trailing comma
      };
      ```
  * Prefer expression-bodied members for simple methods and properties where appropriate.

5.  **Commenting:**
  * Keep comments to an ABSOLUTE MINIMUM. Only comment non-obvious or complex parts of the code.
  * All comments MUST be in English.
  * **NEVER comment `using` directives or namespace declarations** (which are typically at the beginning of a file).

6.  **Error Handling:**
  * Implement robust error handling using `try-catch` blocks for expected exceptions.
  * Use specific exception types rather than catching generic `System.Exception`.
  * Consider custom exceptions for domain-specific errors when beneficial.

7.  **Efficiency & Performance:**
  * Write efficient and performant code.
  * Utilize asynchronous programming (`async`/`await`) for I/O-bound operations or other suitable scenarios to prevent blocking threads.

8.  **Readability & Maintainability:**
  * Prioritize code readability and maintainability. Use clear and descriptive names for variables, methods, classes, etc.
  * Keep methods concise and focused on a single responsibility.

9.  **Dependencies (NuGet Packages):**
  * If external libraries (NuGet packages) are suggested or used, clearly state them, their purpose, and the specific version if critical. If multiple options exist, suggest the most common or best-suited one.

10. **Testing:**
  * When generating code that requires testing (e.g., business logic), keep testability in mind (e.g., favor dependency injection).
  * If asked to generate unit or integration tests, use the **latest version of NUnit**.
  * For assertions in NUnit, use the constraint-based model: `Assert.That(actual, Is.EqualTo(expected))`.

11. **Scope of Changes:**
  * Strictly limit your code modifications to the immediate scope of the current task. DO NOT refactor or alter any code that is not directly related to the specific functionality you are being asked to implement or modify. Avoid making unsolicited changes or "improvements" outside the defined task.

12. **Language Features:**
  * Leverage modern C# features appropriately (e.g., pattern matching, records, collection expressions, `using` declarations) to write concise and expressive code, with the following exception:
  * **NEVER use primary constructors.** Always define constructors explicitly.

13. **Time Abstraction (Mandatory):**
  * **NEVER directly use** `DateTime.Now`, `DateTime.UtcNow`, `DateTimeOffset.Now`, `DateTimeOffset.UtcNow`, `Stopwatch`, or any other system clock-dependent time sources directly.
  * **For regular (non-test) application code, ALWAYS obtain the current time or perform time-related operations through an instance of `TimeProvider`** that is injected via Dependency Injection (DI).
    Example (Conceptual):
    ```csharp
    // In your class
    private readonly TimeProvider _timeProvider;

    public MyService(TimeProvider timeProvider)
    {
      _timeProvider = timeProvider;
    }

    public void DoSomethingWithTime()
    {
      DateTimeOffset utcNowOffset = _timeProvider.GetUtcNow();
      DateTime utcNow = _timeProvider.GetUtcNow().UtcDateTime;
      // ... use 'now' ...
    }

    public ITimer CreateTimer()
    {
      return _timeProvider.CreateTimer(...);
    }
    ```
  * **For test code, ALWAYS use a specific, fixed `DateTime` or `DateTimeOffset` value when dealing with time-sensitive data or logic:**
    * Directly assign constant `DateTime` values when seeding data or initializing entity timestamps (like `CreatedAt`).

14. **Build Verification:**
    * After completing any code generation or modification task, **ALWAYS execute `dotnet build <proj_or_solution>`** to ensure the project compiles successfully.

15. **Test Execution (If Applicable):**
    * If the task involved writing or modifying tests, **ALWAYS execute `dotnet test <arguments>`** after a successful `dotnet build` to ensure all tests pass.
