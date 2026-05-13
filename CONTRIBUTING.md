# Contributing to SemaBuzz

Thank you for your interest in contributing. Please read this document before opening issues or pull requests.

---

## Code of Conduct

Be respectful. Harassment, discrimination, or abusive language toward any contributor will not be tolerated and may result in removal from the project.

---

## License

SemaBuzz.Protocol is open-source under the GNU AGPL v3.0 license. The App and Styles remain proprietary software (Copyright © 2026 Skynr Labs). By submitting a contribution to the Protocol, you agree to license your contribution under the AGPL v3.0.

---

## Branch Model

| Branch | Purpose |
|---|---|
| `main` | Stable, release-ready. Never commit directly here. |
| `dev` | Integration target. All PRs merge here first. |
| `feature/*` | New features (`feature/emoji-reactions`) |
| `fix/*` | Bug fixes (`fix/reconnect-race`) |
| `release/*` | Release stabilization (`release/v1.1`) |

**Flow:** `feature/* / fix/*` → PR to `dev` → PR to `main` → tag release

---

## Opening Issues

Before opening an issue:

- Search existing issues to avoid duplicates.
- For bugs, include: OS version, app version, steps to reproduce, and what you expected vs. what happened.
- For feature requests, describe the problem you are trying to solve, not just the solution.
- For security vulnerabilities, **do not open a public issue** — see [SECURITY.md](SECURITY.md).

---

## Submitting a Pull Request

1. Fork the repo and create your branch from `dev`, not `main`.
2. Name your branch `feature/short-description` or `fix/short-description`.
3. Keep PRs focused — one feature or fix per PR.
4. Ensure the project builds without errors: `dotnet build SemaBuzz.sln`
5. Run the test suite: `dotnet test`
6. Write a clear PR description — what changed and why.
7. Link any related issue in the PR body (`Closes #123`).

PRs targeting `main` directly (except from `dev`) will be automatically closed by CI.

---

## Coding Standards

- **Language:** C# 12, .NET 9
- **Style:** Follow existing patterns in the file you are editing. Do not reformat unrelated code.
- **Naming:** PascalCase for types and members, camelCase for locals. Prefix private fields with `_`.
- **Async:** All I/O must be async. Never use `.Result` or `.Wait()` on a `Task`.
- **Security:** Do not introduce dependencies without discussion. Cryptographic code is especially sensitive — propose changes in an issue first.
- **No dead code:** Do not leave commented-out code in PRs.

---

## Relay Contributions

The relay (`SemaBuzz.Relay`) is the most security-critical component. Changes to relay logic require extra scrutiny:

- The relay must remain a **blind pass-through** — it must never read, log, or store message content.
- Any change to connection handling, room lifetime, or rate limiting must be discussed in an issue first.

---

## Building Locally

```
git clone https://github.com/SemaBuzz/SemaBuzz.git
cd SemaBuzz
dotnet build SemaBuzz.sln -c Debug
dotnet test
```

Set `SemaBuzz.App` as the startup project in Visual Studio to run with F5.

---

## Questions

Open a GitHub Discussion if you have a question that is not a bug or feature request.
