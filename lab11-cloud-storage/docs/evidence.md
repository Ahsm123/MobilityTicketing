# Cloud Storage: evidence

| Scenario | Prediction | Command, result and exit status | What changed in the catalogue and bucket? |
| --- | --- | --- | --- |
| Publish the original | R-001 kommer i kataloget. | `report publish R-001 fixtures/validations-v1.jsonl` udskriver `853f4213-...`, exit 0. | R-001 i kataloget. Én version i bucketen. |
| Interrupt after staging a correction | R-002 ligger kun i bucketen. `verify R-002` giver `UnknownReport`, exit 3. | `report stage R-002 fixtures/validations-v2.jsonl` udskriver `958320f0-...`. `report verify R-002` giver `UnknownReport`, exit 3. | Kataloget uændret. Ny version i bucketen og `receipts/R-002.json`. |
| Recover with a mismatched receipt | | `Sha256` sat til 64 nuller. `report recover R-002 R-001` giver `Mismatch`, exit 1. | Ingenting. |
| Recover with a nonexistent predecessor | FK på `supersedes` stopper den i `SaveAsync`, exit 1. | `report recover R-002 R-999` giver `23503: ... violates foreign key constraint "reports_supersedes_fkey"`, exit 1. | Ingenting. Transaktionen blev rullet tilbage. |
| Recover and repeat recovery | Den anden gør ingenting, exit 0. | `report recover R-002 R-001` to gange. Begge udskriver `958320f0-...`, exit 0. | Første kørsel tilføjer R-002. Anden ændrer ingenting. |
| Verify the original after the correction | | `report verify R-001` og `report verify R-002` giver begge `Matches`, exit 0. | Ingenting. |
| Verify with object storage stopped | | `report verify R-001` giver `ReadFailed`, exit 5. Efter genstart `Matches`, exit 0. | Ingenting. |

## Version IDs and row counts

| Rapport | Version ID | Bytes | Rækker | Supersedes | Nyeste i bucketen |
| --- | --- | --- | --- | --- | --- |
| R-001 | `853f4213-8f91-402b-bcb4-19c7213d2199` | 303 | 3 | | nej |
| R-002 | `958320f0-a8ad-4af8-ad06-8c66921312c5` | 404 | 4 | R-001 | ja |

Begge versioner ligger på samme key. Efter `stage R-002` var kun R-001 i kataloget, fordi en rapport først er
published, når dens katalogrække committer, og `stage` stopper før det.

Når object store er nede, giver `verify` `ReadFailed` (exit 5). Vi fik ingen bytes, så vi ved ikke, om filen er ændret.
Derfor er det en anden kode end `Mismatch` (exit 1), hvor vi læste filen og den var forkert.

Når PostgreSQL er nede, giver `verify` `CatalogueReadFailed` (exit 4). Vi når slet ikke til S3, fordi kataloget skal
fortælle os, hvilken version vi skal læse.

## An additional check

Ved en fejl blev `Sha256` sat til `"0"` i stedet for 64 nuller. `recover` gav `InvalidReceipt`, exit 1, uden at
spørge S3. Kvitteringens format bliver altså tjekket lokalt, før indholdet bliver tjekket mod filen.

## Storage decision

### Ownership

De oprindelige validation-rækker og rapportens metadata ligger i PostgreSQL. Eksportfilerne ligger i object storage,
én version pr. upload. En upload, der venter på at blive published, ligger som kvittering i `receipts/`.

En API, der får en forespørgsel på R-001, slår R-001 op i kataloget og henter præcis version `853f4213-...` med 3
rækker. At R-002's fil nu er den nyeste version på samme key, betyder ikke noget, fordi API'en aldrig spørger efter den
nyeste.

### Safe retries

Ved en fejlet PUT findes der en tom kvittering og måske en version i S3. Kalderen ser `Object upload failed.`, exit 1.
Man skal tjekke bucketen og slette den tomme kvittering, før man kører `stage` igen.

Fejler katalogskrivningen, findes både version og udfyldt kvittering. Kalderen ser databasefejlen, exit 1. `recover` kan
trygt køres igen, fordi den ikke laver en PUT.

Ved et crash før kvitteringen er skrevet, er kvitteringen tom, og der kan ligge en version i S3. Hverken `recover` eller
`stage` virker for det ID, så uploaden skal undersøges manuelt.

Samme recovery kan lykkes to gange, fordi INSERT'en ignorerer en række, der er helt magen til den gemte. En anden
`supersedes` er ikke magen til, så `report recover R-002` uden predecessor gav `ReportConflict`, exit 1, og begge rækker
var uændrede.

### Access and retention

Adgang skal tjekkes i appen, efter rapporten er slået op i kataloget og før filen hentes. Alle rapporter deler samme
key, så S3 kan ikke se forskel på dem.

Appen behøver kun `PutObject` og `GetObjectVersion`. Den skal ikke kunne slette, så en fejl i appen ikke kan fjerne en
published fil.

Et oprydningsjob må aldrig slette en version, der findes i `reports`. En version, der ikke er i kataloget, må kun
slettes, når ingen kvittering peger på den og den er ældre end en karensperiode, for eksempel et døgn. Jobbet kører med
sin egen identitet, der har `DeleteObjectVersion`. Svagheden er, at kvitteringerne ligger lokalt, så jobbet måske kun
kan bruge karensperioden.
