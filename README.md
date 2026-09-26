# pcs7-mcp

Server MCP (Model Context Protocol) per **SIMATIC PCS 7 / STEP 7 V5.x**: permette a un assistente
come Claude Code di leggere e — se abilitato — modificare un progetto PCS 7 attraverso l'interfaccia
di comando di SIMATIC Manager, il database dei chart CFC e il server OPC UA di OpenPCS 7.

Non esiste un server MCP ufficiale Siemens per PCS 7: questo è nato per lavorare su progetti reali
(analisi di programmi AS, export di sorgenti e simboli, lettura dei chart CFC, migrazioni verso TIA Portal).

Sviluppato e provato su **PCS 7 V10.0 SP1 / STEP 7 V5.7**, Windows 10/11 x64.

Funziona in due modi:

- **locale**: PCS 7 è installato sullo stesso PC di Claude Code;
- **remoto**: PCS 7 è su un'altra macchina, tipicamente una VM. Lì si installa **Pcs7Agent**
  (da Windows 7 SP1 a Windows 11 / Server 2022), e il server MCP sul PC gli inoltra le operazioni
  via HTTP con un token. Guida completa: [docs/agente-remoto.md](docs/agente-remoto.md).

## Cosa sa fare

**Lettura** — elenco progetti e multiprogetti, struttura di stazioni e programmi, elenco di blocchi,
sorgenti e chart, proprietà degli oggetti, codice dei blocchi offline (AWL generato da `GenerateSource`),
export di sorgenti, simboli, configurazione hardware e struttura di richiamo, stato CPU online,
contenuto completo dei chart CFC (blocchi, pin, valori, interconnessioni, tag) e lettura di variabili via OPC UA.

**Scrittura** (solo con `--access-mode read-write`, sempre con anteprima e conferma esplicita) —
import e compilazione di sorgenti, compilazione dei chart e dell'hardware, import di simboli e stazioni,
proprietà degli oggetti, salvataggio del progetto, scrittura di una variabile OPC UA.

**Escluso di proposito**: download nel PLC, avvio/arresto CPU, cancellazione di oggetti, memory card,
comandi H-System. Sono operazioni ad alto rischio su impianto e vanno fatte da SIMATIC Manager.

L'elenco completo degli strumenti è in [docs/pcs7-mcp.md](docs/pcs7-mcp.md).

## Come è fatto

- **Pcs7Core** (`src/Pcs7Core`): tutta la logica PCS 7, compilata sia per .NET 8 (server in modalità locale)
  sia per .NET Framework 4.8 (agente). Un unico dispatcher esegue le operazioni per nome, con gli stessi nomi
  dei tool MCP, e rifiuta le scritture se la macchina PCS 7 è in sola lettura.
- **Pcs7McpServer** (`src/Pcs7Mcp`): server MCP stdio per Claude Code; esegue le operazioni in-process
  oppure le inoltra all'agente (`--agent`).
- **Pcs7Agent** (`src/Pcs7Agent`): agente per la macchina PCS 7 con icona nella tray, endpoint HTTP con token
  e installer integrato.
- **Engineering**: interfaccia di comando COM `Simatic.Simatic` (`S7ABATCX.DLL`), in-process a 32 bit:
  per questo il server è compilato **x86**. Tutte le chiamate COM passano da un unico thread STA.
- **Hardware**: interfacce `S7HCOM_X` (stazioni, rack, moduli, indirizzi, export/import `.cfg`).
- **CFC**: lettura diretta del database dei chart tramite l'API interna `s7jdbmox.dll`, in un processo
  figlio separato (`Pcs7CfcReader`), in sola lettura: la transazione viene sempre annullata, mai confermata.
  Le firme delle funzioni sono state ricavate per disassemblaggio e sono documentate in
  [docs/cfc-db-api.md](docs/cfc-db-api.md). Non essendo un'API pubblica Siemens, vanno riverificate
  a ogni aggiornamento di PCS 7.
- **Runtime**: client OPC UA verso `OpcUaServerOpenPCS7` (porta 4863 per impostazione predefinita).

## Requisiti

- PCS 7 / STEP 7 V5.x installato sulla macchina che esegue le operazioni (le interfacce COM sono locali):
  lo stesso PC in modalità locale, la VM con Pcs7Agent in modalità remota
- Sulla VM: .NET Framework 4.8 (Windows 7 SP1 ... 11, Server 2008 R2 SP1 ... 2022, 32 o 64 bit)
- .NET 8 SDK per compilare (`winget install Microsoft.DotNet.SDK.8`)
- Per i chart CFC: editor CFC del progetto **chiuso** durante la lettura
- Per OPC UA: runtime OS attivo e servizio `OpcUaServerOpenPCS7` avviato

## Compilazione

```
.\build.ps1
```

Produce:

- `release\` → `Pcs7McpServer.exe` per il PC (self-contained, .NET 8 x86) e `cfcreader\` per la modalità locale;
- `release\agent\Pcs7Agent-<versione>.zip` → pacchetto da copiare sulla VM (`setup.cmd`, `LEGGIMI.txt`).

La cartella `release/` di questo pacchetto contiene già il risultato della compilazione, utile per provarlo
senza compilare.

## Registrazione in Claude Code

In `%USERPROFILE%\.claude.json`:

```json
{
  "mcpServers": {
    "pcs7": {
      "type": "stdio",
      "command": "C:\\percorso\\pcs7-mcp\\release\\Pcs7McpServer.exe",
      "args": ["--access-mode", "read-write"]
    }
  }
}
```

Senza `--access-mode` il server parte in sola lettura.

Con PCS 7 su una VM si aggiungono l'indirizzo dell'agente e il token (vedi [docs/agente-remoto.md](docs/agente-remoto.md)):

```json
"args": ["--access-mode", "read-only", "--agent", "http://192.168.56.10:8765"],
"env": { "PCS7_MCP_AGENT_TOKEN": "<token mostrato dall'agente sulla VM>" }
```

| Argomento | Variabile d'ambiente | Default |
|---|---|---|
| `--access-mode read-only\|read-write` | `PCS7_MCP_ACCESS_MODE` | `read-only` |
| `--workdir <dir>` | `PCS7_MCP_WORKDIR` | `%LOCALAPPDATA%\pcs7-mcp\export` |
| `--opcua-endpoint <url>` | `PCS7_MCP_OPCUA_ENDPOINT` | `opc.tcp://localhost:4863` (solo modalità locale) |
| `--agent <url>` | `PCS7_MCP_AGENT_URL` | vuoto = modalità locale |
| `--agent-token <token>` | `PCS7_MCP_AGENT_TOKEN` | obbligatorio con `--agent` |
| `--agent-timeout <minuti>` | `PCS7_MCP_AGENT_TIMEOUT` | 30 |
| – | `PCS7_MCP_OPCUA_USER`, `PCS7_MCP_OPCUA_PASSWORD` | accesso anonimo |
| – | `PCS7_MCP_OPCUA_SECURITY=none` | prima si tenta un endpoint sicuro |

Export, log e file generati finiscono nella cartella di lavoro, in una sottocartella per progetto.

## Struttura del pacchetto

```
src/Pcs7Core/         logica PCS 7 condivisa (net48 + net8, x86)
src/Pcs7Mcp/          server MCP (.NET 8, x86), modalità locale o remota
src/Pcs7Agent/        agente per la VM PCS 7 (.NET Framework 4.8, x86) + setup.cmd
src/Pcs7CfcReader/    lettore del database CFC (.NET Framework 4.8, x86)
build.ps1             compila tutto in release/
release/              binari già pubblicati (release/agent/ = zip per la VM)
docs/agente-remoto.md installazione e funzionamento con PCS 7 su VM
docs/pcs7-mcp.md      architettura, elenco strumenti, note operative e test eseguiti
docs/cfc-db-api.md    firme dell'API interna del database CFC
```

## Avvertenze

- Il server agisce su progetti di automazione reali. La modalità di scrittura compila e salva
  sul progetto aperto: usarla solo su copie o con un backup.
- `opc_write` scrive **sul processo in esercizio**: l'anteprima mostra il valore attuale e
  l'operazione richiede una conferma esplicita.
- L'accesso al database CFC usa un'API Siemens non documentata. È in sola lettura, ma resta
  una scelta a proprio rischio.
- Progetto indipendente, non affiliato né supportato da Siemens. SIMATIC, PCS 7, STEP 7 e WinCC
  sono marchi di Siemens AG.

## Licenza

Non ancora definita: finché non viene aggiunto un file `LICENSE`, tutti i diritti sono riservati.
Le librerie di terze parti incluse in `release/` hanno licenze proprie, elencate in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
