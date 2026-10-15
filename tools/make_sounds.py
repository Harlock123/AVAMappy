#!/usr/bin/env python3
"""Synthesises AVAMappy's sound effects and tunes into ../src/AVAMappy/Sounds as 16-bit mono WAV.

Nothing is sampled from any arcade machine: every effect is built here from square, triangle and
noise voices, and the tunes are new compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAMappy", "Sounds")
RNG = random.Random(1982)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # Boing, doors, loot.
    write("Bounce.wav", tone(0.16, 200, 620, "tri", release=0.6, vibrato=0.04, vib_rate=30), 0.35)
    write("Step.wav", tone(0.06, 900, 1300, "pulse", release=0.8), 0.25)
    write("Door.wav", mix(tone(0.12, 160, 110, "square", release=1.0), tone(0.08, 700, 300, "noise", release=1.2, gain=0.4)), 0.35)
    write("Knock.wav", cat(mix(tone(0.12, 260, 120, "square", release=0.8), tone(0.1, 1200, 300, "noise", release=0.9, gain=0.5)), *[tone(0.06, f, kind="tri", release=0.5) for f in [1600, 1900, 1600]]), 0.45)
    write("Wave.wav", mix(tone(0.9, 300, 900, "sine", release=0.4, vibrato=0.1, vib_rate=16), tone(0.9, 600, 1800, "tri", release=0.5, gain=0.4)), 0.4)
    write("Sweep.wav", cat(tone(0.08, 900, 1400, "pulse", release=0.4), tone(0.12, 1400, 2100, "pulse", release=0.6)), 0.35)
    write("Loot.wav", cat(tone(0.06, hz("C6"), kind="pulse", release=0.4), tone(0.1, hz("G6"), kind="pulse", release=0.6)), 0.32)
    write("Pair.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["C6", "E6", "G6", "C7"]], tone(0.25, hz("E7"), kind="pulse", release=0.6)), 0.38)
    write("Snap.wav", mix(tone(0.4, 2400, 300, "noise", release=0.7), tone(0.4, 500, 80, "saw", release=0.6, gain=0.5)), 0.5)
    write("Hurry.wav", cat(*[tone(0.1, f, kind="square", release=0.3) for f in [880, 1180, 880, 1180, 880, 1180]]), 0.35)
    write("Death.wav", cat(*[tone(0.11, hz(n), kind="pulse", release=0.4) for n in ["G5", "E5", "C5", "G4"]], tone(0.5, hz("C4"), kind="tri", release=0.5)), 0.45)
    write("RoundStart.wav", cat(*[tone(0.1, hz(n), kind="pulse", release=0.4) for n in ["G5", "A5", "B5"]], tone(0.4, hz("G6"), kind="pulse", release=0.7)), 0.38)
    write("RoundClear.wav", tune(170, [("G5", 0.5), ("B5", 0.5), ("D6", 0.5), ("G6", 0.5), ("D6", 0.5), ("G6", 0.5), ("B6", 2)], [("G3", 1), ("D3", 1), ("G3", 2)], kind="pulse"), 0.45)
    write("ExtraLife.wav", cat(*[tone(0.08, hz(n), kind="pulse", release=0.3) for n in ["G5", "B5", "D6", "G6", "B6"]]), 0.4)

    # The mansion: a jaunty music-hall tune in G with an oom-pah bass (new). Loops.
    bass_notes = []
    for root, fifth in [("G2", "D3"), ("G2", "D3"), ("C3", "G3"), ("G2", "D3"), ("D3", "A3"), ("D3", "A3"), ("G2", "D3"), ("D3", "A3")] * 2:
        bass_notes += [(root, 0.5), (fifth, 0.5), (root, 0.5), (fifth, 0.5)]
    lead = [("B4", 0.5), ("D5", 0.5), ("G5", 1), ("F#5", 0.5), ("G5", 0.5), ("A5", 1), ("G5", 0.5), ("E5", 0.5), ("C5", 1), ("E5", 1),
            ("D5", 0.5), ("B4", 0.5), ("G4", 1), ("A4", 0.5), ("B4", 0.5), ("C5", 1), ("A4", 1), ("D5", 2),
            ("B4", 0.5), ("D5", 0.5), ("G5", 1), ("F#5", 0.5), ("G5", 0.5), ("B5", 1), ("A5", 0.5), ("G5", 0.5), ("E5", 1), ("C5", 1),
            ("B4", 0.5), ("D5", 0.5), ("A4", 1), ("F#4", 0.5), ("A4", 0.5), ("G4", 3)]
    bass = seq(150, bass_notes, kind="tri", gain=0.5, release=0.5)
    top = seq(150, lead, kind="pulse", gain=0.28, release=0.5)
    write("Music.wav", mix(bass, top), 0.4)

    write("TitleTune.wav", tune(132,
                                [("G4", 0.5), ("B4", 0.5), ("D5", 0.5), ("G5", 0.5), ("E5", 1), ("C5", 1), ("D5", 0.5), ("B4", 0.5), ("A4", 0.5), ("C5", 0.5), ("B4", 2),
                                 ("D5", 0.5), ("E5", 0.5), ("F#5", 0.5), ("G5", 0.5), ("A5", 1), ("F#5", 1), ("G5", 3), ("-", 1)],
                                [("G2", 2), ("C3", 2), ("D3", 2), ("G2", 2), ("D3", 2), ("D3", 2), ("G2", 4)], kind="pulse"), 0.45)
    write("StartTune.wav", tune(160, [("G4", 0.5), ("B4", 0.5), ("D5", 0.5), ("G5", 1), ("D5", 0.5), ("G5", 2)], [("G2", 2), ("D3", 1), ("G2", 2)], kind="pulse"), 0.45)
    write("GameOver.wav", tune(90, [("D5", 1), ("B4", 1), ("G4", 1), ("A4", 1), ("G4", 3)], [("G3", 2), ("E3", 2), ("D3", 1), ("G2", 3)], kind="tri"), 0.45)

if __name__ == "__main__":
    main()
