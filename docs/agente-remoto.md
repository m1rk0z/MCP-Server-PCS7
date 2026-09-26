# PCS 7 su una VM: agente remoto

Quando PCS 7 non è installato sul PC dove gira Claude Code (tipicamente è in una macchina virtuale),
il server MCP lavora in **modalità remota**: resta sul PC, parla con Claude Code via stdio come sempre,
e inoltra ogni operazione a **Pcs7Agent**, un piccolo programma installato sulla VM.

```
PC (Claude Code)                                   VM (PCS 7 / STEP 7 V5.x)
+-------------------------------+   HTTP + token  +----------------------------------+
| Claude Code                   |  -------------> | Pcs7Agent.exe  (icona S7 in tray)|
|   Pcs7McpServer.exe           |   porta 8765    |   Pcs7Core: SIMATIC COM (STA)    |
|   --agent http://VM:8765      |  <------------- |   cfcreader\Pcs7CfcReader.exe    |
|   copie locali degli export   |   risultati     |   client OPC UA -> localhost:4863|
+-------------------------------+   + file        +----------------------------------+
```

La logica PCS 7 (SIMATIC Manager, database CFC, OPC UA) è una sola, nella libreria `Pcs7Core`,
compilata due volte: **.NET 8** per il server in modalità locale e **.NET Framework 4.8** per l'agente.
.NET 4.8 è l'ultima piattaforma che gira da **Windows 7 SP1** a Windows 11 e da Server 2008 R2 SP1
a Server 2022, a 32 e 64 bit: copre i sistemi operativi su cui girano PCS 7 V8.x (Windows 7 / Server 2008 R2),
V9.x (Windows 10 / Server 2016-2019) e V10 (Windows 10/11 / Server 2022).

## Installazione sulla VM

1. Copiare `release\agent\Pcs7Agent-<versione>.zip` sulla VM ed estrarlo **tutto** in una cartella.
2. Eseguire `setup.cmd`. Verifica .NET Framework 4.8 (se manca indica dove scaricarlo) e chiede i diritti
   di amministratore.
3. Nella finestra scegliere:
   - **porta** (predefinita 8765);
   - **modalità**: `read-only` (consigliata) o `read-write`;
   - **IP client consentiti**: l'IP del PC con Claude Code (facoltativo ma consigliato).
4. Alla fine compare la configurazione da usare sul PC (IP, porta, token e blocco per `.claude.json`),
   con i pulsanti per copiarla. Lo stesso testo è in `C:\ProgramData\Pcs7Agent\client-config.txt`.

Installazione non interattiva:

```
setup.cmd --silent --port 8765 --access-mode read-only --allow 192.168.56.1
```

Cosa fa l'installazione (solo strumenti presenti su tutte le versioni di Windows da 7 in poi):

| Voce | Dove |
|---|---|
| Programma | `C:\Program Files (x86)\Pcs7Agent` (`C:\Program Files\...` su Windows a 32 bit) |
| Configurazione e token | `C:\ProgramData\Pcs7Agent\agent.json` (modificabile solo dagli amministratori) |
| Export, upload, log | `C:\ProgramData\Pcs7Agent\export`, `...\export\_uploads`, `...\logs` |
| Prenotazione URL | `netsh http add urlacl url=http://+:PORTA/` (l'agente gira senza diritti di amministratore) |
| Firewall | regola in ingresso "PCS7 MCP Agent" sulla porta TCP (limitata agli IP consentiti, se indicati) |
| Avvio automatico | chiave `HKLM\...\Run` → parte a ogni accesso, **nella sessione dell'utente** |
| Disinstallazione | "Programmi e funzionalità" → PCS7 MCP Agent, oppure `disinstalla.cmd` |

L'agente **non è un servizio Windows** di proposito: l'interfaccia COM di SIMATIC Manager ha bisogno del
profilo utente, delle licenze e di una sessione interattiva, e da servizio (Session 0) spesso non funziona.
Sulla VM quindi deve essere fatto l'accesso con l'utente che usa PCS 7 (anche con login automatico).

Icona nell'area di notifica: **verde** = sola lettura, **arancione** = lettura e scrittura,
**rosso** = non attivo (il motivo è nel fumetto e nel log). Tasto destro: configurazione client,
cartella export, cartella log, esci.

## Configurazione sul PC

In `%USERPROFILE%\.claude.json`:

```json
{
  "mcpServers": {
    "pcs7": {
      "type": "stdio",
      "command": "C:\\percorso\\pcs7-mcp\\release\\Pcs7McpServer.exe",
      "args": ["--access-mode", "read-only", "--agent", "http://192.168.56.10:8765"],
      "env": { "PCS7_MCP_AGENT_TOKEN": "<token mostrato dall'agente>" }
    }
  }
}
```

| Argomento | Variabile d'ambiente | Note |
|---|---|---|
| `--agent <url>` | `PCS7_MCP_AGENT_URL` | senza porta si usa 8765 |
| `--agent-token <token>` | `PCS7_MCP_AGENT_TOKEN` | obbligatorio con `--agent`; meglio nella sezione `env` |
| `--agent-timeout <minuti>` | `PCS7_MCP_AGENT_TIMEOUT` | predefinito 30 (le compilazioni possono essere lunghe) |
| `--workdir <dir>` | `PCS7_MCP_WORKDIR` | le copie degli export vanno in `<workdir>\remote\<host>_<porta>` |

Dopo aver riavviato Claude Code, il tool **`pcs7_status`** mostra se la VM risponde: versione di Windows,
modalità dell'agente, interfaccia SIMATIC disponibile, lettore CFC, cartelle.

## Come funziona

- **Operazioni**: ogni tool MCP diventa `POST /api/invoke/<nome tool>` con gli argomenti in JSON.
  Le anteprime (`confirm=false`) sono calcolate sul PC: sulla VM arrivano solo le operazioni confermate
  (unica eccezione: l'anteprima di `opc_write` legge il valore attuale con `opc_read`).
- **Doppio controllo sulle scritture**: il PC espone i tool di scrittura solo con `--access-mode read-write`,
  e l'agente le rifiuta comunque se è installato in `read-only`. Servono entrambi.
- **File da importare** (`s7_import_source`, `s7_import_symbols`, `s7_import_station`): se il percorso esiste
  sul PC, il file viene inviato con la richiesta (max 20 MB) e salvato in `export\_uploads` sulla VM.
  Un percorso che non esiste sul PC è inteso come percorso sulla VM.
- **File esportati** (sorgenti, simboli, `.cfg`, chart CFC, log di compilazione): l'agente indica i file prodotti,
  il server li scarica in `<workdir>\remote\<host>_<porta>\...` e nel risultato sostituisce il percorso
  della VM con quello locale, così Claude può leggerli anche se sono troppo grandi per la risposta.
- **OPC UA**: il client gira sull'agente e si collega a `opc.tcp://localhost:4863` sulla VM
  (modificabile in `agent.json`: `OpcUaEndpoint`, `OpcUaUser`, `OpcUaPassword`, `OpcUaUseSecurity`).

Protocollo (versione 1), tutte le richieste con `Authorization: Bearer <token>`:

| Richiesta | Risposta |
|---|---|
| `GET /api/health` | informazioni sulla macchina PCS 7 |
| `POST /api/invoke/{operazione}` | `{ success, result \| error, files: [{ relative, path }] }` |
| `GET /api/file?path={relativo}` | contenuto di un file della cartella export (niente al di fuori) |

## Sicurezza

- Token casuale di 256 bit generato all'installazione, confrontato a tempo costante;
  un token errato riceve 401 con mezzo secondo di ritardo; un PC non consentito riceve 403.
- Lista facoltativa degli IP consentiti, applicata sia dall'agente sia dalla regola firewall.
- `agent.json` è modificabile solo dagli amministratori (la modalità read-only non si aggira da un account
  utente), ma è **leggibile dagli utenti locali** della VM perché l'agente gira come utente: contiene il token
  e l'eventuale password OPC UA. Gli utenti possono scrivere solo nelle cartelle `export` e `logs`.
- I file caricati dal PC sono accettati solo dalle operazioni di import e vengono eliminati dopo 7 giorni.
- I download sono confinati nella cartella export.
- Il protocollo non contiene download nel PLC, avvio/arresto CPU, cancellazioni né comandi H-System.
- Ogni richiesta è registrata nel log con IP, operazione, esito e durata.
- **HTTP non è cifrato**: usare una rete host-only o privata tra PC e VM. Per passare da reti non fidate,
  impostare `"ListenHost": "localhost"` in `agent.json` e usare un tunnel SSH o una VPN
  (con un tunnel le richieste arrivano da `127.0.0.1`: se si usa la lista dei client consentiti, aggiungerlo).
  Il server sul PC accetta solo URL `http://`.

## Problemi comuni

| Sintomo | Causa probabile |
|---|---|
| `PCS 7 agent not reachable` | VM spenta, nessun utente collegato (l'agente parte all'accesso), porta chiusa, IP errato |
| `rejected the token` | token nel `.claude.json` diverso da quello in `C:\ProgramData\Pcs7Agent\agent.json` |
| `does not accept requests from this PC` | IP del PC non presente tra i client consentiti |
| icona rossa, "URL reservation is missing" | prenotazione URL mancante: rieseguire `setup.cmd` |
| icona rossa, "Port ... already in use" | un altro programma usa la porta: rieseguire `setup.cmd` con un'altra porta |
| `COM class 'Simatic.Simatic' not registered` | STEP 7 / PCS 7 non installato su quella macchina |
| `STEP 7 S7BIN folder not found` | STEP 7 in un percorso non standard: impostare la variabile di sistema `PCS7_S7BIN` |

## Versioni di PCS 7 precedenti alla V10

Il server è stato sviluppato e provato su PCS 7 V10.0 SP1 / STEP 7 V5.7. Su versioni precedenti:

- l'interfaccia di comando `Simatic.Simatic` esiste da STEP 7 V5.x; le proprietà mancanti in una versione
  (per esempio `UnattendedServerMode`) vengono ignorate;
- **il lettore CFC** usa un'API interna non documentata (`s7jdbmox.dll`, vedi [cfc-db-api.md](cfc-db-api.md))
  le cui firme sono state ricavate sulla V10: su V8/V9 vanno verificate prima di fidarsi dei risultati.
