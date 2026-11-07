# Donald Duck Online Koop

[English](README.md) · **Deutsch**

Online-Koop-Mod für **Disney's Donald Duck: Quack Attack** (PC, deutsche Neuauflage).
Zwei Spieler spielen über das Internet gemeinsam in derselben Welt: zwei Donalds,
gemeinsame Portale, gemeinsame Items, Sterne und Fortschritt, Zuschauer-Modus mit Bildübertragung.

> **Das Spiel ist nicht enthalten.** Jeder Spieler braucht seine eigene CD bzw. ein Abbild
> seiner eigenen CD. Dieses Repository enthält nur den Quellcode des Mods – keine Spieldateien,
> keine CD-Abbilder, keine aus dem Spiel extrahierten Daten.

Inoffizielles Fanprojekt, nicht verbunden mit Disney oder Ubisoft.

## Wie es funktioniert

`DonaldOnline.exe` startet das Original-Spiel und verändert dessen Arbeitsspeicher zur Laufzeit
(zweiter Donald als Kopie, Eingabe nur im Vordergrundfenster, Portal-Sperre, Abgleich von Items,
Fortschritt, Leben und Kamera). Die Verbindung läuft über einen temporären
[Cloudflare Quick Tunnel](https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/do-more-with-tunnels/trycloudflare/)
(`cloudflared` wird beim ersten Online-Spiel von den offiziellen GitHub-Releases geladen).
Der Host bekommt einen Einladungscode, den er dem Mitspieler schickt.

## Benutzen

Fertige EXE + Anleitung: siehe **Releases**. Beide Spieler brauchen genau dieselbe Version
(das Netzwerkprotokoll ändert sich zwischen Versionen).

### Hinweis zu Windows Defender / SmartScreen
Das Programm liest und schreibt den Speicher eines anderen Prozesses und ist nicht signiert.
Windows Defender kann es deshalb als verdächtig melden (z. B. `Behavior:Win32/DefenseEvasion.A!ml`).
Ob du eine Ausnahme einträgst, entscheidest du selbst – am sichersten baust du die EXE aus diesem
Quellcode selbst.

## Bauen

Benötigt nur das .NET Framework 4.x (bei Windows dabei), kein Visual Studio:

```bat
build.cmd
```

Ergebnis: `build\DonaldOnline.exe`. `build.cmd test` baut zusätzlich eine Testversion
(`/define:TESTAUTO`) mit Teststeuerung über Befehlsdateien.

## Dateien

- `src\OnlineLauncher.cs` – Startfenster (Allein spielen, Online-Spiel erstellen/beitreten)
- `src\OnlineSession.cs` – Sitzung, Netzwerkschleife, Levelwechsel, Spielregeln
- `src\RoomWire.cs`, `src\Tunnel.cs`, `src\VideoStream.cs` – Protokoll, Tunnel, Zuschauerbild
- `src\GameSetup.cs`, `src\AppPaths.cs`, `src\GameLocation.cs`, `src\GameInstance.cs` – Einrichtung, Einstellungen, Protokoll
- `src\GameBridge.cs`, `src\ReplicaFrame.cs`, `src\Respawn.cs` – Anbindung an das Spiel, zweiter Donald
- `src\InputGuard.cs`, `src\InputExpressions.cs` – Eingabe-Steuerung
- `src\PortalGate.cs`, `src\LevelExit.cs`, `src\WorldSync.cs`, `src\CoopLives.cs`, `src\Spectator.cs`, `src\NameTag.cs` – Koop-Logik
- `src\TestDriver.cs` – nur in Testversionen
- `resources\dgVoodoo.conf`, `resources\ubi.ini` – eingebettete Einstellungen (dgVoodoo2-Fenstermodus, Spiel-Einstellungen)

## Danksagung

Die Struktur der OpenSpace-Engine (Akteure, Zustände, Eingabe, Always-Objekte) wurde mit Hilfe dieser
Projekte verstanden. Es wurde kein Code daraus übernommen, nur Erkenntnisse über Datenstrukturen:

- [Raymap](https://github.com/byvar/raymap) von byvar und Mitwirkenden (keine Lizenz angegeben)
- [ACP_Ray2](https://github.com/spitfirex86/ACP_Ray2) von spitfirex86 (MIT-Lizenz)
- [dgVoodoo2](https://github.com/dege-diosg/dgVoodoo2) von Dege – wird auf Wunsch von der offiziellen Seite geladen, nicht mitgeliefert

## Lizenz

Noch keine Lizenz festgelegt – alle Rechte am eigenen Code beim Autor.
