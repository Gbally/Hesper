## What and why

<!-- What does this change, and why? Link the issue if there is one: "Fixes #12". -->

## Tried on

- [ ] Mac
- [ ] Windows
- [ ] Mac only or Windows only on purpose (say why below)

## Checklist

- [ ] Meetings stay on the computer: no telemetry, no new network calls besides the ones the user
      asks for (updates, model downloads, their calendar link).
- [ ] No real meeting content anywhere in the change (recordings, transcripts, notes, voices,
      people's names): invented examples only.
- [ ] Engine changes: `.venv/bin/python eval/test_rules.py` passes.
- [ ] New or changed texts are translated (Mac: `python3 scripts/i18n.py extract` lists what's missing).
