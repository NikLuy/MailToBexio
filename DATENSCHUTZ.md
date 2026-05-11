# Datenschutz & AI-Provider

← [Zurück zur README](README.md)

## Ausgangslage

MailToBexio verarbeitet eingehende E-Mails, die **personenbezogene Daten** (Name, Firma, E-Mail-Adresse, Postanschrift) enthalten können. Diese Daten gelten nach dem schweizerischen **Datenschutzgesetz (DSG/nDSG, in Kraft seit 01.09.2023)** als schützenswerte Personendaten und dürfen nur in Länder mit **angemessenem Datenschutzniveau** übermittelt werden.

> **Wichtig:** Die E-Mails liegen in **Microsoft 365 mit Serverstandort Schweiz (Switzerland North)**. Diese Datenhaltung bleibt unabhängig vom gewählten AI-Provider bestehen — der Unterschied liegt darin, wohin der **E-Mail-Inhalt zur KI-Verarbeitung übermittelt** wird.

---

## Übersicht: Datenschutz-Vergleich der drei AI-Varianten

|  | Azure OpenAI / Copilot | Google Gemini | Ollama (lokal) |
|---|---|---|---|
| **Daten verlassen die Schweiz** | ⚠️ **Ja** — ausser Region wird explizit auf Switzerland North gesetzt | ❌ **Ja** — Verarbeitung in USA oder EU | ✅ **Nein** — Verarbeitung ausschliesslich lokal |
| **Serverstandort** | ⚠️ Standard: nicht CH — muss manuell auf **Switzerland North (Zürich)** konfiguriert werden | ❌ Öffentliche API: USA — EU nur via kostenpflichtiges Vertex AI | ✅ Eigener Server (Synology NAS) |
| **Daten in CH, wenn korrekt konfiguriert** | ✅ Ja — nach expliziter Wahl von Switzerland North | ❌ Nein (öffentliche API) | ✅ Ja — immer |
| **Modelltraining mit Kundendaten** | ✅ Nein — vertraglich ausgeschlossen (Enterprise) | ⚠️ Abhängig vom Tarif | ✅ Nein |
| **Auftragsverarbeitungsvertrag (AVV)** | ✅ Im Microsoft Customer Agreement enthalten | ✅ Google Cloud DPA vorhanden | ✅ Nicht erforderlich |
| **DSG/nDSG-Konformität (CH)** | ✅ Ja — **nur mit Region Switzerland North** | ⚠️ Eingeschränkt — SCC erforderlich | ✅ Ja — uneingeschränkt |
| **DSGVO-Konformität (EU)** | ✅ Ja | ✅ Ja | ✅ Ja |
| **Konfigurationsaufwand** | ⚠️ Mittel — Region muss bewusst gewählt werden | ⚠️ Hoch — Vertex AI setup nötig für EU | ✅ Gering |
| **Empfehlung Produktiveinsatz** | ✅ Empfohlen — **mit Switzerland North** | ⚠️ Bedingt | ✅ Empfohlen |

---

## 1. Azure OpenAI / Copilot (Standard)

### Datenfluss

```
O365 Schweiz (Switzerland North)
    │  E-Mail-Body
    ▼
Azure OpenAI (Endpunkt konfigurierbar)
    │  Extrahierte JSON-Daten
    ▼
bexio (Serverstandort: Schweiz)
```

### Datenschutz-Einschätzung

**Serverstandort:** Azure OpenAI Ressourcen werden standardmässig **nicht** in der Schweiz erstellt — die Region muss beim Anlegen der Ressource im Azure Portal **explizit** auf **Switzerland North (Zürich)** gesetzt werden. Bei jeder anderen Region (z.B. East US, West Europe) verlassen die E-Mail-Inhalte die Schweiz. Nach der Erstellung lässt sich die Region einer Ressource nicht mehr ändern.

**Modelltraining:** Microsoft verwendet Eingabedaten von Azure OpenAI Enterprise-Kunden **nicht** zum Trainieren von Modellen. Dies ist im [Microsoft-Produktvertrag (MPA)](https://aka.ms/DPA) und den Azure OpenAI Servicebedingungen festgehalten.

**Auftragsverarbeitungsvertrag:** Die Microsoft Online Service Terms (OST) bzw. der Microsoft Customer Agreement (MCA) enthält die notwendigen Auftragsverarbeitungsklauseln gemäss Art. 9 DSG/nDSG und Art. 28 DSGVO. Kein separater Vertrag nötig, wenn ein bestehender Microsoft 365-Vertrag vorhanden ist.

**Zu beachten:**
- Den Azure OpenAI Endpunkt explizit in der Region **Switzerland North** erstellen
- Nicht die globale `api.openai.com`-Adresse verwenden (das wäre OpenAI direkt, nicht Azure)
- In `.env`: `COPILOT_ENDPOINT=https://your-resource.openai.azure.com/` — die URL enthält die Region implizit über den Ressourcennamen

**Fazit:** ✅ Bei korrekter Konfiguration (Switzerland North) vollständig DSG/nDSG- und DSGVO-konform. Empfohlen für den Produktiveinsatz.

---

## 2. Google Gemini

### Datenfluss

```
O365 Schweiz (Switzerland North)
    │  E-Mail-Body
    ▼
Google Gemini API (Rechenzentrum: USA oder EU)
    │  Extrahierte JSON-Daten
    ▼
bexio (Serverstandort: Schweiz)
```

### Datenschutz-Einschätzung

**Serverstandort:** Die öffentliche Google Gemini REST API (`generativelanguage.googleapis.com`) verarbeitet Anfragen primär in **US-amerikanischen Rechenzentren**. Eine Standortsteuerung ist nur über **Google Cloud Vertex AI** (kostenpflichtig, separates Produkt) möglich, welches EU-Regionen unterstützt.

**Drittlandübermittlung:** Die USA gelten gemäss Schweizer DSG/nDSG **nicht** als Land mit angemessenem Datenschutzniveau (keine Äquivalenzentscheidung des EDÖB für die USA). Eine Übermittlung ist dennoch zulässig, wenn:
- **Standardvertragsklauseln (SCC)** abgeschlossen werden, oder
- Eine der anderen Ausnahmen nach Art. 17 DSG greift

Google bietet SCC im Rahmen seiner [Google Cloud Data Processing Terms](https://cloud.google.com/terms/data-processing-addendum) an. Für die öffentliche Gemini API (nicht Vertex AI) gelten die [Google AI Additional Terms of Service](https://ai.google.dev/terms) — diese sind weniger ausführlich als die Cloud-DPA.

**Modelltraining:** Bei der öffentlichen Gemini API werden Eingaben standardmässig möglicherweise zur Modellverbesserung verwendet. Dies lässt sich in den API-Einstellungen deaktivieren, ist jedoch nicht standardmässig ausgeschaltet.

**Fazit:** ⚠️ Für den Produktiveinsatz mit personenbezogenen Daten aus der Schweiz **nur bedingt empfohlen**. Wenn Google gewünscht wird: **Google Cloud Vertex AI** mit EU-Region und DPA verwenden statt der öffentlichen Gemini API. Rücksprache mit dem Datenschutzbeauftragten empfohlen.

---

## 3. Ollama (lokal)

### Datenfluss

```
O365 Schweiz (Switzerland North)
    │  E-Mail-Body
    ▼
Ollama (läuft auf dem Synology NAS, intern)
    │  Extrahierte JSON-Daten
    ▼
bexio (Serverstandort: Schweiz)
```

### Datenschutz-Einschätzung

**Serverstandort:** Das Sprachmodell läuft direkt auf dem **Synology NAS im eigenen Netzwerk**. E-Mail-Inhalte verlassen das interne Netzwerk zu keinem Zeitpunkt für die KI-Verarbeitung.

**Modelltraining:** Kein externes Modelltraining — das Modell (z.B. Llama 3) ist statisch und wird lokal ausgeführt. Keine Verbindung nach aussen.

**Auftragsverarbeitungsvertrag:** Nicht erforderlich, da keine Daten an Dritte übermittelt werden.

**Einschränkungen:**
- Die Qualität der Datenextraktion ist abhängig vom gewählten lokalen Modell (Llama 3, Mistral etc.) und in der Regel geringer als bei GPT-4o oder Gemini
- Der Synology NAS benötigt ausreichend RAM (min. 8 GB für Llama 3 8B, 16 GB+ für bessere Modelle)
- Verarbeitungsgeschwindigkeit ist deutlich langsamer als Cloud-APIs (kein GPU-Einsatz auf Standard-NAS)

**Fazit:** ✅ Aus Datenschutzsicht die **beste Variante** — keine Daten verlassen das eigene Netzwerk. Empfohlen wenn der Datenschutz höchste Priorität hat oder Mails besonders sensible Personendaten enthalten.

---

## Handlungsempfehlungen

### Welche Variante für welchen Anwendungsfall?

| Szenario | Empfehlung |
|---|---|
| Standard-Geschäftspost, gute Extraktionsqualität gewünscht | **Azure OpenAI** (Switzerland North) |
| Maximaler Datenschutz, keine Drittanbieter | **Ollama** lokal |
| Gemini bereits lizenziert, EU-Standort vorhanden | **Google Vertex AI** (EU-Region, nicht öffentliche API) |
| Testbetrieb / Entwicklung | **Ollama** oder **Gemini** (öffentliche API) |

### Allgemeine Massnahmen (unabhängig vom AI-Provider)

1. **Minimalprinzip:** Nur die nötigen E-Mail-Felder an die KI senden (aktuell: `Body.Content`). Betreff und Metadaten nicht übermitteln wenn nicht nötig.
2. **Logging:** Log-Dateien enthalten keine vollständigen E-Mail-Inhalte (nur IDs und Statusmeldungen) — nicht verändern.
3. **Fehler-Ordner:** Mails im Ordner `Fehler` enthalten möglicherweise sensible Daten — Zugriff einschränken und regelmässig bereinigen.
4. **Secrets:** API-Keys sicher verwahren (`.env` nicht ins Git), Zugriff auf den NAS auf berechtigte Personen beschränken.
5. **Dokumentation:** Diese Verarbeitung im Verzeichnis der Bearbeitungstätigkeiten nach Art. 12 DSG/nDSG führen.

### Verzeichnis der Bearbeitungstätigkeiten (Muster-Eintrag nach Art. 12 nDSG)

| Feld | Inhalt |
|---|---|
| **Bezeichnung** | Automatisierte Kundenkontakt-Erfassung aus E-Mails |
| **Verantwortlicher** | [Firma, Adresse] |
| **Zweck** | Automatische Anlage von Kundenkontakten in bexio aus eingehenden E-Mails |
| **Datenkategorien** | Name, Vorname, Firmenname, E-Mail-Adresse, Postanschrift, Telefonnummer |
| **Betroffene** | Kunden und Interessenten, die E-Mails senden |
| **Empfänger** | bexio AG (Schweiz), AI-Provider (abhängig von Variante, siehe oben) |
| **Aufbewahrung** | Gemäss bexio-Konfiguration; verarbeitete Mails werden als gelesen markiert |
| **Technische Massnahmen** | Verschlüsselte Übertragung (TLS), App-Only Auth, Secret Management via Umgebungsvariablen |

---

← [Zurück zur README](README.md)
