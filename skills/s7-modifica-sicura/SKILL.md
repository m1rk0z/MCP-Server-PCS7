---
name: s7-modifica-sicura
description: Procedura per modificare un programma STEP 7 V5 / PCS 7 (S7-300/400) già in produzione tramite il server MCP pcs7, senza perdere le modifiche fatte in campo e senza mai scaricare in CPU. Usala quando l'utente chiede di cambiare, correggere o aggiungere logica in FC/FB/DB di un progetto SIMATIC Manager, di importare o compilare un sorgente AWL/SCL, o di preparare una modifica da scaricare su un impianto.
---

# Modifica sicura di un programma S7 in produzione

Il server `pcs7` può importare, compilare e salvare sul **progetto offline**. Non scarica mai in CPU:
il download lo fa sempre una persona da SIMATIC Manager. Questa skill serve a far sì che ciò che
arriva al download sia esattamente ciò che si voleva.

## Regole fisse

- Nessuno strumento di scrittura (`s7_import_source`, `s7_compile_source`, `s7_import_symbols`,
  `s7_save`, `opc_write`) senza un **ok esplicito** dell'utente in chat per quella operazione.
  Prima si chiama sempre con `confirm=false` (anteprima) e si mostra l'esito.
- Il download in CPU non si propone come azione dell'agente: si prepara, si descrive, lo fa l'utente.
- Indicare il progetto con il **percorso**, non con il nome: in SIMATIC Manager possono esistere due
  progetti con lo stesso nome e uno dei due è spesso una copia vecchia.

## Procedura

1. **Identificare il progetto e il programma.**
   `s7_list_projects` (con `details`) → scegliere per percorso e data; chiedere conferma se ci sono
   omonimi. `s7_project_structure` → il programma giusto è quello sotto la stazione della CPU in
   questione (nei progetti PCS 7 ci sono molti programmi di slave e CP).
2. **Backup.** Chiedere all'utente un archivio datato del progetto e, se possibile, un "Carica nel PG"
   della CPU in un progetto separato. Non proseguire senza.
3. **Confronto online/offline (lo fa l'utente in SIMATIC Manager).** Chiedere il report del confronto
   e leggerlo così:
   - data diversa ma checksum uguale → blocco identico;
   - checksum diverso con lo stesso codice → spesso solo una dichiarazione diversa (BYTE contro BOOL,
     BOOL contro DINT) in un DB: **non scaricare quel DB**, si perdono i valori attuali;
   - blocchi presenti solo in CPU o diversi nel codice → modifiche fatte in campo: si lavora sul
     progetto che le contiene e ci si riportano sopra i propri sorgenti;
   - "progettazione offline modificata ma non compilata" → l'esito sugli SDB non è affidabile:
     compilare e rifare il confronto.
4. **Leggere prima di scrivere.** `s7_read_block_code` sui blocchi da toccare (con
   `includeUsedBlocks` se serve il contesto) e `s7_export_symbols`. Le risposte grandi finiscono su
   file nella cartella di lavoro: analizzarle da lì.
5. **Controllare le trappole** (vedi sotto) prima di scegliere merker, timer, DB e indirizzi nuovi.
6. **Scrivere il sorgente** AWL/SCL in un file, con versione nel titolo del blocco (`VERSION`) e un
   commento che dica cosa cambia. Conservare il sorgente originale accanto (`_prima.awl`).
7. **Import e compilazione**, ognuno prima in anteprima e poi con `confirm=true` dopo l'ok:
   `s7_import_source` (`overwrite` solo se richiesto) → `s7_compile_source` → leggere il log del
   compilatore (errori **e** avvisi) → `s7_save`.
8. **Rileggere** con `s7_read_block_code` il blocco compilato e confrontarlo con il sorgente.
9. **Consegna all'utente**: elenco esatto dei blocchi da scaricare, quelli da **non** scaricare (DB
   con valori di processo) e una VAT di verifica (skill `s7-diagnosi-vat`).
10. **Dopo il download** chiedere un nuovo confronto online/offline: è la prova che in CPU è entrato
    solo ciò che si voleva.

## Trappole note

- **Merker "liberi":** cercare anche gli accessi a byte, word e doppia word che li coprono
  (`MW200` usa `M200.x` e `M201.x`). Un merker che sembra libero può essere scritto da un encoder.
- **Timer e contatori:** verificare che non siano usati in nessun altro blocco del programma, anche
  senza simbolo.
- **Cambiare la struttura di un DB** lo reinizializza al download: preferire un DB nuovo o un DB di
  appoggio parallelo. Un dato nuovo parte da 0 dopo il download: dirlo a chi lo legge.
- Un blocco KOP/FUP ricompilato da sorgente AWL ha la stessa logica ma cambia linguaggio.
- Nelle word S7 il byte alto è il primo: bit 8..15 nel byte n, bit 0..7 nel byte n+1 (vale anche per
  le word di allarme dei pannelli).
- Fotocellule di presenza spesso attive a 0: un filo interrotto sembra "occupato".
- Ogni bit di trasferimento con autoritenuta deve avere un timeout.
- SDB copiati dall'online nella cartella Blocchi non aggiornano NetPro: alla prima compilazione
  vengono rigenerati con i dati vecchi.
- Simboli doppi sullo stesso indirizzo non si cancellano dall'interfaccia di comando: solo a mano
  nell'editor simboli.
- La configurazione DP deve corrispondere ai moduli montati: aggiungere byte senza il modulo mette il
  nodo in errore.
