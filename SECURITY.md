# Security Policy

## Reporting a vulnerability

Please report security issues **privately** through GitHub's
[Private vulnerability reporting](https://docs.github.com/en/code-security/security-advisories/guidance-on-reporting-and-writing-information-about-vulnerabilities/privately-reporting-a-security-vulnerability)
on this repository, rather than opening a public issue.

This is a one-person project. Expect an acknowledgement within a week; a fix
depends on severity and on what else is happening.

## Why this project warrants more care than a typical utility

Read this before deciding whether to install it, and before reviewing the code:

**1. A text service is loaded into every application that accepts text.**
WordStrip optionally registers a Windows Text Services Framework text service —
a native DLL that Windows loads into Chrome, Word, Explorer and everything else
you type in. A defect in it can affect those applications, not just WordStrip.
The DLL is deliberately minimal for exactly this reason: it reads the text before
the caret, sends it over a pipe, and does nothing else. It does not modify
documents.

**2. Registration is machine-wide and needs administrator rights.**
That is a Windows constraint, not a choice — every text service on a stock
Windows 11 install is registered under `HKLM`. WordStrip's ordinary installation
is per-user and needs no elevation; registering the text service is a separate,
explicit step.

**3. The application itself must never run elevated.**
A keyboard hook installed by an elevated process cannot see input going to
non-elevated windows. Beyond breaking the app, running it elevated would give a
process that reads your keystrokes more privilege than it needs.

**4. It sees what you type.**
That is what it is for. What it does with it is bounded deliberately:

- No network calls at runtime. The only outbound request is the optional model
  download, which the user initiates.
- No telemetry, no analytics, no identifiers.
- By default nothing typed is written to disk at all. With learning enabled, what
  is written is *counts* of words, pairs and triples — never text.
- Suggestions, autocorrect and learning are all disabled in password fields.
- The text service sends at most 128 characters of context, and the wire format
  has nowhere to put more.
- Everything it stores lives in `%LOCALAPPDATA%\WordStrip` as plain files you can
  read and delete.

## Known security-relevant limitations

Stated plainly rather than left to be discovered:

- **Binaries are unsigned.** SmartScreen will warn on first run. Code signing for
  an individual developer outside the US and Canada is currently impractical, and
  since 2024 an EV certificate no longer bypasses the warning anyway. Verify the
  source and build it yourself if that matters to you.

  An unsigned binary means you're trusting the download, not a certificate chain,
  so verify what you're actually running: every [release](../../releases) lists a
  SHA-256 checksum for each file. Check it before running anything, especially the
  installer, which needs no elevation to run but does install a component that can
  later be granted it (see "Browser and Office Support" in Settings):

  ```powershell
  Get-FileHash .\WordStrip-Setup-*.exe -Algorithm SHA256
  ```

  A mismatch means the file was modified or corrupted in transit — don't run it.
  This checks integrity, not authorship: it confirms the file matches what this
  repository's releases actually published, not that a specific person built it.
- **Password-field suppression relies on the host.** TSF has no explicit
  "password field" flag. WordStrip honours `GUID_COMPARTMENT_KEYBOARD_DISABLED`,
  which is the mechanism applications are meant to use, and this has been tested
  in Chrome and Word. An application that does not set it would not be detected.
- **The named pipe is scoped to the logon session and ACL'd to the current user.**
  Another account on the same machine cannot connect to it. A process running as
  the same user can, and could feed it false context — the worst outcome being
  wrong suggestions.

## Scope

In scope: anything that lets an attacker read or influence what a user types,
escalate privilege, break the host application, or extract stored data.

Out of scope: SmartScreen warnings on unsigned binaries (known, above), and
suggestion quality.
