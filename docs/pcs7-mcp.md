# Server MCP PCS 7 V10 SP1 (pcs7-mcp)

Server MCP sviluppato su misura per far lavorare Claude Code con SIMATIC PCS 7 V10.0 SP1
installato su questa macchina (non esiste un server MCP pubblico per PCS 7).

## Installazione

| Voce | Valore |
|---|---|
| Eseguibile | `C:\Tools\pcs7-mcp\bin\Pcs7McpServer.exe` (.NET 8, **x86**, self-contained) |
| Sorgenti | `C:\Tools\pcs7-mcp\src\Pcs7Mcp` |
| Registrazione | `%USERPROFILE%\.claude.json` â†’ `mcpServers.pcs7` (livello utente) |
| ModalitÃ  | `--access-mode read-write` |
| Cartella export/log | `<workdir>` (sottocartella per progetto, `_logs`, `_opcua_pki`) |
| Prerequisito build | .NET 8 SDK (installato con winget, `Microsoft.DotNet.SDK.8`) |

Ricompilare e ripubblicare:

```
cd C:\Tools\pcs7-mcp\src\Pcs7Mcp
dotnet publish -c Release -o C:\Tools\pcs7-mcp\bin
```

(chiudere prima la sessione di Claude Code che usa il server, altrimenti l'exe Ã¨ bloccato)

### Argomenti / variabili d'ambiente

| Argomento | Variabile | Default |
|---|---|---|
| `--access-mode read-only\|read-write` | `PCS7_MCP_ACCESS_MODE` | read-only |
| `--workdir <dir>` | `PCS7_MCP_WORKDIR` | `<workdir>` |
| `--opcua-endpoint <url>` | `PCS7_MCP_OPCUA_ENDPOINT` | `opc.tcp://localhost:4863` |
| â€“ | `PCS7_MCP_OPCUA_USER` / `PCS7_MCP_OPCUA_PASSWORD` | anonimo |
| â€“ | `PCS7_MCP_OPCUA_SECURITY=none` | tenta prima un endpoint sicuro |

## Architettura

- **Engineering** â†’ interfaccia di comando COM di SIMATIC Manager `Simatic.Simatic`
  (`S7ABATCX.DLL`, in-proc 32 bit â†’ per questo il processo Ã¨ x86).
  Tutte le chiamate COM passano da un unico thread STA.
  `UnattendedServerMode = true` (nessuna finestra di dialogo) e `VerbLogFile` impostato
  (i messaggi di compilazione finiscono nel log invece che in popup).
- **Hardware** â†’ interfacce `S7HCOM_X` (stazioni, rack, moduli, indirizzi, export/import .cfg).
- **CFC/SFC** â†’ tramite SIMATIC Manager (cartella Charts: elenco, proprietÃ , compilazione).
  Il modello a oggetti interno del CFC (`CFC-Plan`, `S7JCFCAX.EXE`) **non Ã¨ istanziabile
  dall'esterno** (CO_E_SERVER_EXEC_FAILURE) e l'Automation Interface .NET del CFC Ã¨ un
  framework interno non documentato: blocchi/interconnessioni dei chart non sono modificabili.
- **Runtime** â†’ client OPC UA verso il server OpenPCS 7 (`OpcUaServerOpenPCS7`, porta 4863).

Enumerazione collezioni: si usa `_NewEnum` (IEnumVARIANT). `Item(indice)` sulla collezione
progetti costa ~2 s per chiamata (misurato), l'enumeratore ~4 s per 98 progetti.
L'elenco progetti Ã¨ in cache per 10 minuti.

## Strumenti

### Lettura (sempre disponibili)

| Tool | Cosa fa |
|---|---|
| `s7_list_projects` | Progetti/multiprogetti (librerie opzionali), filtro testo, `details` per autore/commento/data |
| `s7_project_structure` | Stazioni e programmi S7 con cartelle blocchi/sorgenti/chart e conteggi |
| `s7_list_objects` | Blocchi, sorgenti o chart CFC/SFC di un programma (paginato, filtro) |
| `s7_object_details` | ProprietÃ  di un blocco/sorgente/chart |
| `s7_read_block_code` | Codice dei blocchi offline via `GenerateSource` (file AWL su disco, progetto non modificato) |
| `s7_export_source` | Export sorgente AWL/SCL/GRAPH |
| `s7_export_symbols` | Export tabella simboli (sdf/asc/dif/seq) |
| `s7_station_hardware` | Rack, moduli, MLFB, firmware, indirizzi I/O |
| `s7_export_station` | Export configurazione HW in .cfg |
| `s7_export_program_structure` | Struttura di richiamo dei blocchi (DIF) |
| `s7_cpu_state` | Stato operativo CPU **online** (RUN/STOP) |
| `opc_status` / `opc_browse` / `opc_read` | Stato server, navigazione e lettura variabili di processo |
| `cfc_list_charts` | Chart CFC dal database CFC: nome, commento, numero e tipi di blocchi (filtro sul nome) |
| `cfc_read_charts` | Contenuto completo dei chart indicati: istanze (nome, tipo, FB/FC, commento), pin con direzione, tipo, valore, flag `changed`, collegamenti `from`/`to` (`chart\blocco.pin`) e `tag` (simbolo/indirizzo) |
| `cfc_export_charts` | Stesso contenuto per tutti i chart (o filtrati) in un file JSON in `export\<progetto>\cfc\`; restituisce solo il riepilogo |

### Lettura dei chart CFC (aggiunta 17/09/2026)

Nessuna interfaccia ufficiale esporta il contenuto dei chart (XML di SIMATIC Manager = solo HW/tag/Plant View,
VXM senza export, Automation Interface "Comos PT"/"PCS7 ES" â†’ `Error` fuori da SIMATIC Manager).
Soluzione: lettura diretta del database CFC con l'API C interna **`s7jdbmox.dll`** (non documentata, firme ricavate
per disassemblaggio su CFC V10.0 SP1). Dettagli in [cfc-db-api.md](cfc-db-api.md).

- Eseguibile separato `C:\Tools\pcs7-mcp\bin\cfcreader\Pcs7CfcReader.exe` (net48 x86, sorgenti `C:\Tools\pcs7-mcp\src\Pcs7CfcReader`),
  avviato dal server come processo figlio: un crash della DLL Siemens non ferma il server.
- **Sola lettura**: una transazione aperta e sempre annullata (`gl_TransactionAbort`), mai confermata.
- Database: `<cartella progetto>\ES_LOC\<n>` (uno per cartella chart). Tenere **chiuso l'editor CFC** del progetto durante la lettura.
- Uso diretto: `Pcs7CfcReader.exe export --project-dir <dir .s7p> [--charts a,b] [--filter txt] [--changed-only] [--no-sinks] --out file.json`
- Verificato su <progetto di prova>: 250 chart, 4953 blocchi, 51 s per l'export completo; chart <chart di prova> identico a quanto ricavato da DB di istanza e riferimenti incrociati.
- Limiti noti: posizione grafica/fogli, run sequence e interfaccia dei chart (I/O del chart) non ancora letti; i tipi strutturati APL sono indicati come `codeNN` (es. `code43`).

### Scrittura (solo `read-write`, sempre anteprima â†’ `confirm=true`)

| Tool | Cosa fa |
|---|---|
| `s7_import_source` | Importa file .awl/.scl nella cartella Sorgenti |
| `s7_compile_source` | Compila una sorgente (crea/sovrascrive blocchi) |
| `s7_compile_charts` | Compila tutti i chart CFC/SFC del programma |
| `s7_compile_station` | Controllo consistenza o compilazione HW |
| `s7_import_symbols` | Importa simboli (insert / overwrite-name / overwrite-operand) |
| `s7_import_station` | Importa stazione da .cfg |
| `s7_set_object_properties` | Commento/autore/famiglia di un oggetto |
| `s7_save` | Salva le modifiche fatte via interfaccia di comando |
| `opc_write` | Scrive un valore nel **processo in esercizio** |

### Escluso volutamente

Download/caricamento nel PLC, avvio/arresto CPU, cancellazione di oggetti, memory card,
comandi H-System: operazioni ad alto rischio su impianto, da fare da SIMATIC Manager.

## Note operative

- Nei progetti PCS 7 ci sono molti "programmi" (slave DP, CPâ€¦). Il programma AS vero di solito
  Ã¨ quello con la cartella Charts (es. `<programma AS>` in `<progetto PCS 7>`).
- I nomi progetto non sono univoci (es. due `<progetto di prova>esar_Prj`): in quel caso usare il percorso.
- Le cartelle hanno nomi localizzati (Blocks/Blocchi/Bausteine): i tool usano il tipo
  (`blocks`, `sources`, `charts`), non il nome.
- OPC UA: serve il runtime OS attivo (oggi il servizio `OpcUaServerOpenPCS7` Ã¨ in pausa).

## Test eseguiti (16/09/2026)

| Tool | Esito |
|---|---|
| `s7_list_projects` | OK, 5 s (prima 187 s con `Item(indice)`) |
| `s7_project_structure` | OK (`<progetto PCS 7>`: stazione AS3, programma `<programma AS>` con 6851 blocchi, 2 sorgenti, 446 chart) |
| `s7_list_objects` (chart) | OK, 4 s |
| `s7_read_block_code` | OK (esempio Siemens ZDt01_01, FC1) |
| `s7_export_symbols` | OK (`<programma AS>.sdf`, 231 kB) |
| `s7_station_hardware` / `s7_export_station` | OK. In AS3 risultano solo PS 407 e CP 443-1: la CPU non Ã¨ nella configurazione HW di quel progetto (confermato anche dal .cfg) |
| `opc_status` | connessione rifiutata: runtime OS / `OpcUaServerOpenPCS7` non attivo |

### Test di scrittura sul programma `Prove` di `<progetto PCS 7>` (autorizzati dall'utente)

File di test: `<workdir>\test\` (`MCP_Test_FC990.awl`, `MCP_Test.sdf`)

| Tool | Esito |
|---|---|
| `s7_import_source` (anteprima) | OK, nessuna modifica |
| `s7_import_source` (confirm) | OK, sorgente `MCP_Test_FC990` creata |
| `s7_import_source` (secondo import senza overwrite) | bloccato correttamente |
| `s7_compile_source` | OK, `0 Error(s), 0 Warning(s)` â†’ creato **FC990** |
| `s7_import_symbols` (insert) | OK, simbolo `MCP_Test_FC` = FC 990 |
| `s7_set_object_properties` | OK, commento della sorgente modificato |
| `s7_compile_charts` su `Prove` | errore gestito: il programma non ha cartella Charts |
| `s7_save` | OK; verificato con una nuova istanza del server (FC990, simbolo e codice presenti) |
| `s7_compile_station`, `s7_import_station`, `opc_write` | non eseguiti |

**Pulizia (16/09/2026, su richiesta dell'utente):** blocco `FC990` e sorgente `MCP_Test_FC990`
rimossi via interfaccia di comando e salvati (`Prove` = OB1, DB56, DB58, nessuna sorgente).
Il simbolo `MCP_Test_FC` (FC 990) **resta** nella tabella simboli di `Prove`: l'interfaccia di comando
non permette di cancellare un singolo simbolo (solo l'intera tabella). Da eliminare a mano nell'editor simboli.

## Analisi API (file di appoggio)

Dump delle typelib generati durante lo sviluppo: `docs/`.


