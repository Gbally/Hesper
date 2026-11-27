# Privacy

Hesper records, transcribes and summarises your meetings **on your computer** (Mac, Windows or
Linux). Audio, transcripts, notes, meeting titles, names and voiceprints are never sent anywhere by
Hesper.

## What stays on your computer

| What | Where | How long |
|---|---|---|
| Recordings and voice samples (`mic` and `system` audio files) | the notes folder | deleted after 7 days by default (Settings → Privacy and storage) |
| Transcripts and notes | the notes folder | until you delete them |
| Known voices (`voices.json`) | Hesper's folder | until you forget them (Settings → Voices) |
| Settings | the app's preferences (Mac), `settings.json` in Hesper's folder (Windows, Linux) | until you remove Hesper |

| | Notes folder (default) | Hesper's folder |
|---|---|---|
| Mac | `~/Documents/MeetingNotes` | `~/Library/Application Support/Hesper` |
| Windows | `Documents\MeetingNotes` (`%USERPROFILE%\MeetingNotes` when OneDrive syncs Documents) | `%LOCALAPPDATA%\Hesper` |
| Linux | `MeetingNotes` in your Documents folder | `~/.local/share/Hesper` |

If you choose a OneDrive (or other synced) folder for your notes, your own sync client uploads
them to your own account — Hesper itself doesn't. On Windows and Linux, a meeting is only moved
there once it has been recorded.

## What goes over the network

- **No analytics, no tracking.** Hesper has no usage statistics, crash reporting or
  identifiers of any kind. (Versions 0.3.1 to 0.5.1 sent optional anonymous usage counts; they
  were removed in 0.5.2.)
- **Updates and model list**: once a day the app reads its update file (`latest.json`,
  `latest-windows.json` or `latest-linux.json`) and `models.json` from this GitHub repository.
- **First setup and model downloads**: the engine (Python and its packages, and llama.cpp on
  Windows and Linux) comes from the Python package index and GitHub, the AI models from
  [Hugging Face](https://huggingface.co).
- **Email drafts in a browser** (only if you pick Gmail or Outlook on the web in Settings): the notes
  are passed to that website to create the draft. The default mail app, Outlook and Apple Mail
  drafts stay local.
- **A calendar link** (only if you paste one in Settings → Recording → Calendar): that link, and
  nothing else, is read every 15 minutes. Nothing is sent to it.

## Permissions it asks for

- **Mac**: Microphone (your voice), Screen & System Audio Recording (the other participants'
  voices — sound only, the screen is never recorded), Calendar (optional: meeting titles and
  attendees, read locally), Reminders (only when you export to-dos), Notifications.
- **Windows and Linux**: none. Hesper uses the microphone and the computer's sound as the system
  allows desktop apps to. On Windows it can read classic Outlook's calendar (optional, read
  locally).

## Removing everything

Settings → Privacy and storage → **Remove Hesper** deletes the engine, the AI models, the
settings and learned voices, and removes the app (and, if you tick it, moves your notes to the
Trash or the Recycle Bin).

Questions: [open an issue](https://github.com/Gbally/Hesper/issues).

Always tell the other participants that you're recording, and follow your company's rules and the
law where you are.
