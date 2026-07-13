# WASABI

**W**indows **A**udio **S**chema **B**lock **I**nterface — router audio visuale per Windows.

WASABI permette di costruire schemi a blocchi per redirigere l'audio: cattura selettiva da applicazioni, mix, split e invio verso più uscite (cuffie, altoparlanti del monitor, AUX, ecc.).

## Caso d'uso tipico

> Far uscire **gioco + Discord** contemporaneamente da **cuffie** e **altoparlanti**.

```
[Gioco] ──┐
          ├── [Mixer] ── [Splitter] ──┬── [Cuffie]
[Discord] ┘                           └── [Altoparlanti]
```

![Schema a blocchi WASABI: gioco e Discord su cuffie e altoparlanti](docs/images/wasabi-routing-example.png)

1. Aggiungi blocchi **App** per gioco e Discord, configurandoli con ⚙
2. Collega entrambi a un **Mixer**
3. Collega il mixer a un **Splitter**
4. Collega ogni uscita dello splitter a un blocco **Uscita** (cuffie / altoparlanti)
5. Clic **▶ Avvia routing**

## Calibrazione automatica delle uscite

Due periferiche diverse, per esempio cuffie USB e monitor HDMI, possono avere latenze hardware differenti.

1. Configura almeno due blocchi **Uscita** e ferma il routing.
2. Clicca **Calibra latenza…** e scegli un microfono che possa sentire entrambi gli output.
3. Avvia il test: WASABI emette tre brevi chirp, separatamente su ogni uscita.
4. Controlla i valori proposti e clicca **Applica ritardi**.
5. Riavvia il routing.

La calibrazione usa cross-correlazione FFT/GCC-PHAT per confrontare l'arrivo del segnale al microfono e ritarda l'uscita più veloce. La compensazione manuale in ⚙ su ciascuna uscita rimane disponibile per le rifiniture.

Un microfono esterno vicino al punto d'ascolto è più affidabile. Se il test indica un segnale debole, alza il volume e ripeti in un ambiente silenzioso.

## Requisiti

- Windows 10 (2004+) o Windows 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

> La cattura per-app usa l'API **Process Loopback** di Windows (disponibile da Windows 10 2004).

## Build e avvio

```powershell
cd c:\Users\angry\Projects\audiohandler
dotnet restore Wasabi.sln
dotnet build Wasabi.sln -c Release
dotnet run --project src\Wasabi.App\Wasabi.App.csproj -c Release
```

## Blocchi disponibili

| Blocco | Descrizione |
|--------|-------------|
| **App** | Cattura l'audio di un'applicazione specifica (gioco, Discord, browser…) |
| **Dispositivo (loopback)** | Cattura tutto l'audio che esce da un dispositivo |
| **Mixer** | Somma più sorgenti in un unico segnale |
| **Splitter** | Duplica un segnale su più uscite |
| **Bus virtuale** | Punto di passaggio interno nel grafo |
| **Uscita** | Invia l'audio a un dispositivo fisico (cuffie, HDMI, AUX…) |

## Collegamenti

1. Clic su una porta **OUT** (arancione)
2. Clic su una porta **IN** (blu) del blocco destinazione
3. Trascina i blocchi per riorganizzare lo schema

## Patch

Salva e ricarica le configurazioni in formato `.wasabi.json`.  
Un esempio è in `samples/game-discord-dual-output.wasabi.json`.

## Periferiche virtuali

Windows non permette di creare vere periferiche audio virtuali da user-mode senza un driver kernel firmato. WASABI risolve il caso d'uso in modo diverso:

- **Routing diretto**: cattura per-app e invia a più uscite fisiche senza cavo virtuale
- **Bus virtuale**: nodo interno al grafo per organizzare il segnale
- **Cavi virtuali esterni** (VB-Cable, VoiceMeeter…): rilevati come dispositivi normali nei blocchi Loopback/Uscita

## Note

- Configura app e dispositivi con ⚙ su ogni blocco prima di avviare
- Durante il routing l'editor è bloccato; premi **Stop** per modificare
- Se un'app non produce audio, la cattura resta silenziosa (comportamento WASAPI)

## Licenza

MIT
