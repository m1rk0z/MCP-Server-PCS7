# Librerie di terze parti

La cartella `release/` contiene il risultato di `dotnet publish` in modalità self-contained:
oltre agli assembly di questo progetto include il runtime .NET e le dipendenze elencate qui.
Ogni componente resta soggetto alla propria licenza.

| Componente | Versione | Licenza |
|---|---|---|
| .NET 8 runtime (`System.*`, `coreclr`, `clrjit`, `hostfxr`, …) | 8.x | MIT — Microsoft |
| Microsoft.Extensions.Hosting e pacchetti collegati | 10.x | MIT — Microsoft |
| ModelContextProtocol (`ModelContextProtocol.dll`, `ModelContextProtocol.Core.dll`) | 2.2.0 | MIT |
| Microsoft.Extensions.AI.Abstractions | — (dipendenza di ModelContextProtocol) | MIT — Microsoft |
| OPC UA .NET Standard client (`Opc.Ua.*`) | 1.5.378.176 | OPC Foundation — RCL (Reciprocal Community License); uso libero per membri OPC Foundation e per software che interagisce con prodotti certificati OPC |
| Newtonsoft.Json | — (dipendenza) | MIT — James Newton-King |
| BitFaster.Caching | — (dipendenza) | MIT |

Nessun file di Siemens AG è incluso nel pacchetto. Il server usa le interfacce COM e le DLL
dell'installazione PCS 7 / STEP 7 presente sulla macchina (`S7ABATCX.DLL`, `S7HCOM_X`, `s7jdbmox.dll`),
che non vengono ridistribuite.

Prima di pubblicare il repository conviene verificare che la licenza RCL della libreria OPC UA
sia compatibile con l'uso previsto; in alternativa si può rimuovere `release/` dal repository
e distribuire i soli sorgenti, lasciando che sia NuGet a scaricare le dipendenze in fase di build.
