---
name: s7-diagnosi-vat
description: Diagnosi in sola lettura di un PLC S7-300/400 con STEP 7 V5 o PCS 7, tramite il server MCP pcs7. Prepara tabelle delle variabili (VAT) in testo da incollare in SIMATIC Manager, imposta registrazioni online in sola lettura per guasti intermittenti e analizza il codice dei blocchi. Usala quando l'utente chiede perché una macchina non parte, perché un allarme compare o sparisce, cosa fa un bit o un blocco, o vuole una VAT per verificare una modifica.
---

# Diagnosi in sola lettura e VAT

Prima si capisce, poi si tocca. Questa skill non scrive nulla: né in CPU né nel progetto.

## Procedura

1. **Progetto e programma** come in `s7-modifica-sicura` (percorso, non nome). `s7_cpu_state` dice se
   la CPU è raggiungibile e in RUN.
2. **Risalire alla causa nel codice.** `s7_read_block_code` sul blocco sospetto con
   `includeUsedBlocks=true`; `s7_export_symbols` per i nomi; `s7_export_program_structure` per sapere
   chi chiama chi. Per ogni condizione che blocca, annotare l'operando esatto.
3. **Preparare la VAT** con gli operandi trovati (formato sotto) e salvarla come `VAT_<argomento>.txt`.
4. **Guasti intermittenti:** prima registrare, poi modificare. Proporre una registrazione in sola
   lettura (per esempio un logger `snap7` che legge a intervalli fissi gli stessi operandi della VAT
   e scrive un CSV con l'ora) e incrociarla con lo storico allarmi del pannello. Spesso la causa è
   un sensore o una parte meccanica, non il software.
5. **Riferire** che cosa blocca, con l'operando e il valore atteso, e che cosa resta da verificare in
   campo. Nessuna scrittura: se un valore va forzato lo fa l'utente da VAT.

## Formato della VAT in testo

SIMATIC Manager accetta righe incollate nella colonna "Operando" di una tabella delle variabili.
Un operando per riga, separato dal formato con una tabulazione; i commenti su una riga a parte che
inizia con `//`:

```
// ===== <IMPIANTO> - <ARGOMENTO> (<blocco e versione>) =====
// Uso: SIMATIC Manager > Inserisci > Blocco S7 > Tabella delle variabili, incolla nella colonna
// "Operando", poi Variabile > Collega a CPU.

// --- Condizioni di avvio ---
// Passo attuale della sequenza
MW 20	DEC
// Fotocellula ingresso (0 = pallet presente)
E 12.3	BOOL
// Timer di sosta
T 120	SIMATIC_TIME
// Valore dal DB di interfaccia
"DB_INTERFACCIA".Altezza	DEC
DB200.DBB 100	CHARACTER
```

- Usare il simbolo tra virgolette quando esiste, l'indirizzo assoluto altrimenti.
- Raggruppare per funzione con un commento `// ---` e mettere in alto ciò che si guarda per primo.
- Formati utili: `BOOL`, `DEC`, `HEX`, `BIN`, `CHARACTER`, `SIMATIC_TIME`, `FLOATING_POINT`.
- Ingressi, uscite e periferia con la notazione italiana o tedesca del progetto (`E`/`A` oppure
  `I`/`Q`, `PEW`): controllare l'impostazione mnemonica nei sorgenti esportati.

## Limiti

- Lo screenshot o la lettura di una VAT vede solo le righe in primo piano: per un'ora di
  osservazione serve un logger, non una persona davanti al monitor.
- Un PLC raggiungibile solo in MPI/PROFIBUS dal PG non si legge con `snap7`: lì si usa la VAT.
- `opc_read` funziona solo con il runtime OS di PCS 7 attivo.
