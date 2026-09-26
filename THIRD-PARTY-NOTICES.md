# Librerie di terze parti

La cartella `release/` contiene il risultato della compilazione:

- `release/` — server MCP per il PC, pubblicato in modalità self-contained (runtime .NET 8 incluso);
- `release/agent/Pcs7Agent-<versione>.zip` — agente per la macchina PCS 7 (.NET Framework 4.8, non incluso:
  è un componente di Windows).

Oltre agli assembly di questo progetto sono incluse le dipendenze elencate qui.
Ogni componente resta soggetto alla propria licenza; le licenze indicate sono quelle dichiarate
nei pacchetti NuGet delle versioni usate.

| Componente | Versione | Licenza | Dove |
|---|---|---|---|
| .NET 8 runtime (`System.*`, `coreclr`, `clrjit`, `hostfxr`, …) | 8.x | MIT — Microsoft | server |
| Microsoft.Extensions.* (Hosting, Logging, DependencyInjection, Options, …) | 10.x | MIT — Microsoft | server, agente |
| Pacchetti di compatibilità .NET (`System.Text.Json`, `System.Memory`, `System.Buffers`, `Microsoft.Bcl.*`, …) | vari | MIT — Microsoft | agente |
| ModelContextProtocol (`ModelContextProtocol.dll`, `ModelContextProtocol.Core.dll`) | 2.2.0 | MIT | server |
| Microsoft.Extensions.AI.Abstractions | — (dipendenza di ModelContextProtocol) | MIT — Microsoft | server |
| OPC UA .NET Standard client (`Opc.Ua.*`) | 1.5.378.176 | OPC Foundation MIT License 1.00 | server, agente |
| BouncyCastle.Cryptography | 2.6.2 (dipendenza di OPC UA su .NET Framework) | MIT — Legion of the Bouncy Castle | agente |
| Newtonsoft.Json | 13.0.4 | MIT — James Newton-King | server, agente |
| BitFaster.Caching | 2.6.0 (dipendenza di OPC UA) | MIT — Alex Peck | server, agente |

Le licenze MIT richiedono di mantenere l'avviso di copyright e il testo della licenza
nelle copie distribuite: i testi completi sono nei rispettivi pacchetti NuGet.

Nota: versioni più vecchie della libreria OPC UA .NET Standard erano distribuite con doppia licenza
RCL (membri OPC Foundation) / GPL 2.0; la versione usata qui (1.5.378.176) è sotto licenza MIT.
Verificarlo di nuovo quando si aggiorna il pacchetto.

Nessun file di Siemens AG è incluso nel pacchetto. Il server e l'agente usano le interfacce COM e le DLL
dell'installazione PCS 7 / STEP 7 presente sulla macchina (`S7ABATCX.DLL`, `S7HCOM_X`, `s7jdbmox.dll`),
che non vengono ridistribuite.
