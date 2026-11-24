# Security

Hesper records meetings, so security reports matter a lot. Thank you for looking.

## Reporting a problem

Please **don't open a public issue**. Report it privately:
[Security → Report a vulnerability](https://github.com/Gbally/Hesper/security/advisories/new).
Hesper is a one-person project (for now): I'll answer as soon as I can, and once
it's fixed the release notes say what was fixed (and credit you, if you like).

Please include the Hesper version, Mac or Windows, what someone could do with the problem, and how
to reproduce it. Never include real meeting recordings, transcripts or notes — yours or anyone's.

## Supported versions

Only the latest release gets fixes. Hesper updates itself (Settings → About → Check now), so please
check you're on the latest one first.

## What counts

Hesper's promise is that meetings stay on your computer. Anything that breaks it is in scope, for
example:

- meeting audio, transcripts, notes or voice profiles leaving the computer, or readable by other
  accounts on the same computer;
- Hesper downloading or running something it shouldn't: updates, model downloads, `hesper://`
  links, the calendar link;
- a meeting file (or a calendar) that makes Hesper do something unexpected.

Not in scope: someone who already controls your user account (they can read your files anyway), and
the "unknown developer" warnings — known: the apps aren't signed or notarized yet.
