---
name: pcs7-cfc-lettura
description: Lettura e analisi dei chart CFC di un progetto PCS 7 tramite il server MCP pcs7 (cfc_list_charts, cfc_read_charts, cfc_export_charts). Usala quando l'utente chiede come è collegato un blocco in un chart, da dove arriva un segnale, quali istanze usano un tipo di blocco, quali parametri sono stati cambiati rispetto al default, o vuole documentare i chart di un'area.
---

# Lettura dei chart CFC

I chart si leggono direttamente dal database CFC del progetto, in sola lettura (la transazione viene
sempre annullata). Non si modifica nulla: interconnessioni e parametri dei chart si cambiano solo
dall'editor CFC.

## Prima di iniziare

- Chiedere all'utente di **chiudere l'editor CFC** del progetto durante la lettura.
- Il programma AS giusto è quello con la cartella Charts: verificarlo con `s7_project_structure`.

## Procedura

1. `cfc_list_charts` con un filtro sul nome per trovare i chart dell'area: nome, commento, numero e
   tipi di blocchi.
2. `cfc_read_charts` sui chart che interessano: istanze, pin con direzione, tipo, valore, flag
   `changed` e collegamenti `from`/`to` nella forma `chart\blocco.pin`, più i `tag` collegati.
   Di default (`changedOnly = true`) restituisce solo i pin modificati o collegati; con `false`
   arrivano tutti i pin, commenti compresi.
3. Per un'analisi su molti chart usare `cfc_export_charts`: scrive un JSON nella cartella di lavoro
   e restituisce solo il riepilogo; poi analizzare il file (per esempio con Python) invece di
   caricarlo tutto nella conversazione.
4. Per seguire un segnale: partire dal pin, seguire `from` all'indietro fino a un ingresso di
   periferia o a un tag, e in avanti con `to` fino all'uscita o all'OS.
5. I pin con `changed = true` sono i parametri impostati a mano: sono quelli da riportare in una
   documentazione o da confrontare fra due impianti.

## Limiti noti

- Posizione grafica, fogli, run sequence e interfaccia del chart non vengono letti.
- I tipi strutturati della libreria APL compaiono come `codeNN`.
- Le risposte grandi superano il limite di una risposta e vengono salvate su file.
- Per il codice dei blocchi (FB/FC) chiamati dai chart si usa `s7_read_block_code`.
