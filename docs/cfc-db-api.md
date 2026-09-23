# API database CFC (s7jdbmox.dll) â€” firme ricavate

PCS 7 V10.0 SP1, `C:\Program Files (x86)\SIEMENS\STEP7\S7BIN\s7jdbmox.dll` (x86, `cdecl`).
Non documentata da Siemens: firme ricavate per disassemblaggio (Capstone) e verificate sul progetto <progetto di prova> il 17/09/2026.
Se si aggiorna CFC/PCS 7 vanno riverificate.

## Convenzioni
- Tutte le funzioni restituiscono `UINT64` = codice di stato (0 = OK). L'ultimo parametro Ã¨ `WORD session`.
- Gli ID oggetto sono `UINT64`.
- Iteratori: `MODE` 1 = primo, 2 = successivo. **L'ultimo elemento arriva con stato â‰  0** (low word `0xA0`, `0xA3`, `0xA5`, `0xAA`)
  ma con ID e dati validi: si termina quando l'ID restituito Ã¨ 0 oppure dopo aver letto l'elemento con stato â‰  0.
- Livelli sottostanti: `s7jdbmsx.dll` (`srv_*`), `s7jdotsx.dll` (DB a oggetti DOTS, `tms_*`/`rms_*`).

## Sequenza
```c
gl_RegisterApp();                                   // ()
gl_TaskConnect("NomeApplicazione");                 // (const char*)  - NULL provoca access violation
gl_TransactionBegin(&session, 0xFFFF);              // (WORD* out, WORD)
?gl_ProjectIdGet@@YA_KPBDAA_KG@Z(path, &prj, s);    // path = <progetto>\ES_LOC\<n>
?gl_CpusGet@@YA_KW4MODE@@_KAA_KAAUOutCpu@@G@Z(mode, prj, &cpu, OutCpu*[0x124], s);   // OutCpu+0 = nome ("Charts")
?gl_ChartsGet@@YA_KW4MODE@@_KAAUOutChart@@AA_KG@Z(mode, cpu, OutChart*, &chart, s); // OutChart+4 = nome chart
gl_ObjectCommentGet(id, char* [0x101], s);
iea_CfcObjectsGetV6_0(mode, chart, int sheet /*0xFFFF = tutti*/, UINT64* obj, OutObj*[0xE4], s);
iea_ParasGetV6_0(mode, block, int filter, UINT64* para, OutPara*[0x8E0], s);
iea_ConnectedParaPathGetV6_1(mode, para, UINT64* other, OutPath*[0x31A], s);
gl_ParaValueGet2(para, char* value, s);
gl_TransactionAbort(session);                       // (WORD) - mai Commit
```

## Strutture
**OutObj (0xE4)**: +0x00 int kind (1 = FB, 2 = FC), +0x04 tipo, +0x24 nome istanza, +0x4C commento.

**OutPara (0x8E0)** con `filter = 1`:
| Offset | Campo |
|---|---|
| 0x000 | codice tipo dato (0 STRUCT, 1 BOOL, 2 BYTE, 4 WORD, 5 INT, 6 DWORD, 8 REAL, 43 struttura APL â€¦) |
| 0x006 | nome pin (25) |
| 0x01F | prefisso struttura padre (es. `PV.`) |
| 0x120 | flag: bit1 valore diverso dal default del tipo, bit2 collegato |
| 0x130 | valore (testo; BOOL vuoto â†’ usare `gl_ParaValueGet2`) |
| 0x234 | direzione 1 IN, 2 OUT, 3 IN_OUT |
| 0x23C | 1 = pin di primo livello, 0 = membro di struttura |
| 0x240 | visibile |
| 0x3C1 | commento pin |
| 0x444 / 0x476 / 0x4A8 | collegamento a indirizzo: simbolo / indirizzo (es. `IW866`) / commento simbolo |
| 0x5D0 | sorgente collegamento `chart\blocco.pin` (per OUT contiene un solo partner: usare `iea_ConnectedParaPathGetV6_1`) |

**OutPath (0x31A)**: +0x000 pin, +0x101 blocco, +0x118 chart.

## Filtri di `iea_ParasGetV6_0`
1 `srv_SelectParas` (tutti i pin, valori, collegamenti â€” usato), 2/20 `srv_SelectPlugs`, 3 `srv_SelectPlugsEdit`,
4 `srv_SelectAllPlugs` (solo pin parametrizzabili), 5 `srv_SelectParasWithS7_m_c`, 17 `srv_SelectPlugsPtv`; altri â†’ errore.

## Funzioni scartate
- `gl_BlocksGet`: lavora su contenitori C++ interni (crash con parametri semplici).
- `gl_OperandPathGet`: restituisce il percorso del pin stesso, non l'operando collegato.
- `iea_CfcObjectsGetV6_0` con `sheet` â‰  0xFFFF: errore `â€¦0D` (numero foglio non valido).

