"""A calendar link (ICS / webcal): Google Calendar's "secret address", the new Outlook's published
calendar, iCloud's public calendar… Read by the app instead of (or with) the system calendar: on
Windows and Linux it is the only calendar besides classic Outlook. Only the link the user pasted
is fetched."""

import re
import urllib.request
from datetime import datetime, timedelta, timezone

ONLINE = ("teams.microsoft.com", "zoom.us", "webex.com", "meet.google.com", "whereby.com", "gotomeeting.com")


def fetch_events(url: str, days: int = 2, now: datetime | None = None) -> list[dict]:
    """Timed events from yesterday to `days` days ahead, recurring ones expanded, cancelled ones
    left out. Times are ISO 8601 with their UTC offset."""
    url = re.sub(r"^webcals?://", "https://", url.strip(), flags=re.I)
    if not re.match(r"^https?://", url, re.I):
        raise ValueError("The calendar link must start with https:// or webcal://")
    req = urllib.request.Request(url, headers={"User-Agent": "Hesper (calendar link)"})
    with urllib.request.urlopen(req, timeout=30) as r:  # noqa: S310 — the link the user pasted
        data = r.read(20_000_000)
    return parse_events(data, days, now)


def parse_events(data: bytes | str, days: int = 2, now: datetime | None = None) -> list[dict]:
    import icalendar
    import recurring_ical_events

    cal = icalendar.Calendar.from_ical(data)
    now = now or datetime.now(timezone.utc)
    start = (now - timedelta(days=1)).replace(hour=0, minute=0, second=0, microsecond=0)
    end = now + timedelta(days=days)
    out = []
    for e in recurring_ical_events.of(cal).between(start, end):
        dtstart, dtend = e.get("DTSTART"), e.get("DTEND")
        if dtstart is None or not isinstance(dtstart.dt, datetime):  # all-day: not a meeting
            continue
        if str(e.get("STATUS", "")).upper() == "CANCELLED":
            continue
        s = _aware(dtstart.dt)
        f = _aware(dtend.dt) if dtend is not None and isinstance(dtend.dt, datetime) else s + _duration(e)
        text = " ".join(str(e.get(k, "")) for k in ("LOCATION", "DESCRIPTION", "URL", "X-GOOGLE-CONFERENCE")).lower()
        uid = str(e.get("UID", ""))
        out.append({
            "title": str(e.get("SUMMARY", "")).strip(),
            "start": s.isoformat(timespec="seconds"),
            "end": f.isoformat(timespec="seconds"),
            "organizer": _person(e.get("ORGANIZER"))["name"] if e.get("ORGANIZER") else "",
            "attendees": [_person(a) for a in _list(e.get("ATTENDEE"))
                          if str(a.params.get("CUTYPE", "INDIVIDUAL")).upper() not in ("ROOM", "RESOURCE")],
            "online": any(d in text for d in ONLINE),
            "id": f"{uid}@{s.isoformat(timespec='seconds')}",
            "series": uid,
        })
    out.sort(key=lambda x: x["start"])
    return out


def _aware(dt: datetime) -> datetime:
    """Floating times (no time zone) are the computer's local time."""
    return dt if dt.tzinfo else dt.astimezone()


def _duration(e) -> timedelta:
    d = e.get("DURATION")
    return d.dt if d is not None and isinstance(d.dt, timedelta) else timedelta(hours=1)


def _list(x) -> list:
    return [] if x is None else x if isinstance(x, list) else [x]


def _person(p) -> dict:
    email = re.sub(r"^mailto:", "", str(p), flags=re.I).strip()
    name = str(p.params.get("CN", "")).strip().strip('"') if hasattr(p, "params") else ""
    return {"name": name or email.split("@")[0], "email": email if "@" in email else ""}


if __name__ == "__main__":  # quick check: python -m hesper.ics URL
    import json
    import sys

    print(json.dumps(fetch_events(sys.argv[1]), indent=1, ensure_ascii=False))
