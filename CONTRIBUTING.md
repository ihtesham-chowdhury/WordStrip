# Contributing to WordStrip

Thanks for looking. This is a small project maintained by one person, so a short
issue describing what you want to change is usually better than a large surprise
pull request.

---

## The licensing bit, first, because it affects you

WordStrip is released under the **Apache License 2.0**.

**By submitting a contribution you agree to two things:**

1. Your contribution is licensed to the project and its users under the Apache
   License 2.0 — the same terms as the rest of the project.
2. You also grant the copyright holder a perpetual, worldwide, irrevocable,
   royalty-free right to license your contribution under **other terms**,
   including commercial or proprietary ones.

Point 2 exists because the maintainer may offer WordStrip commercially in
future. Without it, every accepted contribution would permanently constrain what
the project as a whole can be licensed as, and a single contributor who became
uncontactable could block it forever.

**What this does not do:** it does not take your copyright away. You keep it.
You can use your own contribution however you like, including in other projects.
It grants a licence, not ownership.

If you are not comfortable with point 2, please say so in the issue before
writing code — there may be another way to get your change in, and it is better
to find out first.

> This is a plain-English summary written by the maintainer, not a lawyer, and
> it has not been reviewed by one. If you are contributing on behalf of an
> employer, or the contribution is substantial enough that it matters to you,
> get your own advice before submitting.

### Sign-off

Every commit must carry a `Signed-off-by` line certifying the
[Developer Certificate of Origin](https://developercertificate.org/):

```
git commit -s -m "your message"
```

---

## Before you write code

- **Open an issue.** Especially for anything touching the prediction stack, the
  input path, or the text service. Several things in this codebase look like
  ordinary code and are load-bearing in non-obvious ways.
- **Read the comments around what you are changing.** They explain *why*, and in
  several places record approaches that were tried and failed. That is
  deliberate — it saves the next person from repeating them.

## Things that will get a change rejected

These are project rules, not preferences:

- **`WordStrip.Core` must not gain a NuGet dependency.** It has none, on purpose.
  It is why the prediction stack is predictable and why a broken package cannot
  take the app down. Runtime dependencies live in separate projects behind an
  interface, the way ONNX does.
- **No telemetry, no analytics, no phone-home.** Including anonymous counters.
- **Nothing that stores or transmits user text.** Counts of words, pairs and
  triples are the maximum, and only with the user's explicit consent.
- **Nothing that blocks a keyboard hook or a TSF callback.** No disk I/O, no
  model loading, no inference. These run on other applications' UI threads; a
  blocking call there is a frozen Chrome.
- **No suggestions, corrections or learning in password fields.** Ever.

## Testing

- Unit tests: `dotnet test tests/WordStrip.Core.Tests/WordStrip.Core.Tests.csproj`
- The suite is 364 tests and is expected to be green before and after your change.
- Input and UI behaviour is verified end to end by
  `tests/regression/Verify-PersistentBar.ps1`, which drives a real Win32 edit
  control. It takes over the keyboard for about a minute. Anything a unit test
  cannot reach belongs there.
- Performance claims are measured, not asserted. `PerformanceTests` and
  `MemoryProfileTests` exist for this; if you change the prediction stack, say
  what the numbers did.

## Style

Match the surrounding code. In particular: comments explain **why**, not what.
A comment restating the line below it will be asked to justify itself.
