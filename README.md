# pcs7-mcp

Server MCP (Model Context Protocol) per **SIMATIC PCS 7 / STEP 7 V5.x**: permette a un assistente
come Claude Code di leggere e — se abilitato — modificare un progetto PCS 7 attraverso l'interfaccia
di comando di SIMATIC Manager, il database dei chart CFC e il server OPC UA di OpenPCS 7.

Non esiste un server MCP ufficiale Siemens per PCS 7: questo è nato per lavorare su progetti reali
(analisi di programmi AS, export di sorgenti e simboli, lettura dei chart CFC, migrazioni verso TIA Portal).

Sviluppato e provato su **PCS 7 V10.0 SP1 / STEP 7 V5.7**, Windows 10/11 x64.

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

- PCS 7 V10.0 SP1 oppure STEP 7 V5.7 installato sulla stessa macchina (le interfacce COM sono locali)
- .NET 8 SDK per compilare (`winget install Microsoft.DotNet.SDK.8`)
- Per i chart CFC: editor CFC del progetto **chiuso** durante la lettura
- Per OPC UA: runtime OS attivo e servizio `OpcUaServerOpenPCS7` avviato

## Compilazione

```
cd src\Pcs7Mcp
dotnet publish -c Release -o ..\..\release
cd ..\Pcs7CfcReader
dotnet publish -c Release -o ..\..\release\cfcreader
```

La cartella `release/` di questo pacchetto contiene già il risultato della pubblicazione
(self-contained, .NET 8 x86), utile per provarlo senza compilare.

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

| Argomento | Variabile d'ambiente | Default |
|---|---|---|
| `--access-mode read-only\|read-write` | `PCS7_MCP_ACCESS_MODE` | `read-only` |
| `--workdir <dir>` | `PCS7_MCP_WORKDIR` | `%LOCALAPPDATA%\pcs7-mcp\export` |
| `--opcua-endpoint <url>` | `PCS7_MCP_OPCUA_ENDPOINT` | `opc.tcp://localhost:4863` |
| – | `PCS7_MCP_OPCUA_USER`, `PCS7_MCP_OPCUA_PASSWORD` | accesso anonimo |
| – | `PCS7_MCP_OPCUA_SECURITY=none` | prima si tenta un endpoint sicuro |

Export, log e file generati finiscono nella cartella di lavoro, in una sottocartella per progetto.

## Struttura del pacchetto

```
src/Pcs7Mcp/          server MCP (.NET 8, x86)
src/Pcs7CfcReader/    lettore del database CFC (.NET Framework 4.8, x86)
release/              binari già pubblicati
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
