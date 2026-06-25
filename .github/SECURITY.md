# Security Policy

MS Community Plugins (MSCP) runs inside Milestone XProtect™: recording servers,
event servers, and operator clients. We take security seriously and appreciate
responsible disclosure.

## Supported versions

Only the **latest release** receives security fixes. There is no back-porting to
older versions. Please update to the newest release before reporting an issue.

| Version            | Supported |
| ------------------ | --------- |
| Latest release     | ✅        |
| Older releases     | ❌        |

## Reporting a vulnerability

**Please do not open a public issue for security vulnerabilities.**

Report privately through GitHub's **Private Vulnerability Reporting**:

1. Go to the [Security tab](https://github.com/Cacsjep/mscp/security) of the repository.
2. Click **Report a vulnerability**.
3. Describe the issue, affected component/version, and steps to reproduce.

This opens a private channel visible only to the maintainers. If you cannot use
GitHub, reach out via the [Discord](https://discord.gg/Geu5daeGPE) and ask a
maintainer to open a private advisory, and **do not post details publicly**.

## What to include

- Affected plugin / driver / installer and version.
- A clear description of the impact (what an attacker can do).
- Steps to reproduce or a proof of concept.
- Any relevant logs, configuration, or environment details.

## Our commitment

- We aim to **acknowledge** a report within **3 business days**.
- We aim to provide an initial assessment within **7 business days**.
- We practice **coordinated disclosure**: we fix the issue, publish a
  [GitHub Security Advisory](https://github.com/Cacsjep/mscp/security/advisories),
  and credit the reporter (unless you prefer to remain anonymous).
- Confirmed fixes ship in the next release and are noted in the
  [Changelog](https://cacsjep.github.io/mscp/support/changelog/) as **Security** entries.

## Scope

In scope: all plugins, device drivers, standalone applications, and the installer
in this repository.

Out of scope: vulnerabilities in Milestone XProtect™ itself (report those to
Milestone Systems), and issues that require physical access or pre-existing
administrative compromise of the host.

Thank you for helping keep the community safe.
