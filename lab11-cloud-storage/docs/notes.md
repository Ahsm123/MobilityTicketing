# Lab 11: noter

## Opgave 2: projektgrænser

### Skitse

```
CLI (Program.cs)
 └─ ReportWorkflow                      Application ejer interfaces (Ports.cs)
     │
     │ stage
     │  1. Reserve       UploadReceipts       tom fil i receipts/
     │  2. Upload        S3ReportObjectStore  PUT til S3, får versionId
     │     ── GAP: objektet er i S3, kvitteringen er tom ──
     │  3. Write         UploadReceipts       versionId, sha256 og rækker i filen
     │
     │ recover
     │  4. Read          UploadReceipts       læs kvitteringen
     │  5. ReadVersion   S3ReportObjectStore  hent versionen og tjek sha256 og rækker
     │  6. Save          ReportCatalogue      INSERT i PostgreSQL
     │     ── CATALOGUE COMMIT: ReportCatalogue.cs:62 ──
     │
     │ verify
     │  7. Find          ReportCatalogue      slå rapporten op
     │  8. ReadVersion   S3ReportObjectStore  hent versionen og tjek den
```

Midterkolonnen er adapters, altså de klasser i Infrastructure, der implementerer interfaces fra Application.
`publish` kører bare `stage` og derefter `recover`.

### Hvem ejer hvad

Application definerer, hvad den har brug for, som interfaces i `Ports.cs`. Infrastructure implementerer dem til S3,
PostgreSQL og lokale filer. Application kender derfor ikke S3 eller Npgsql, så man kan skifte provider eller teste
med falske adapters uden Docker. Prisen er et ekstra lag og flere filer.

### Hvornår er en rapport published?

Når transaktionen committer på `ReportCatalogue.cs:62`. Før det kan ingen andre se rækken, og fejler noget, bliver
den rullet tilbage. `verify` slår altid op i kataloget først, så den finder kun published rapporter.

### Kvitteringen

Kvitteringen er en lille log over en upload, der venter på at blive published. Den husker `versionId`, checksum og
rækkeantal, så `recover` kan gøre arbejdet færdigt efter et crash.

`Reserve` opretter en tom fil, før vi uploader. Kun én proces kan oprette filen, så to `stage` med samme ID kan ikke
uploade samtidig.

### Svaghed: gappet

Dør processen mellem upload og `Write`, ligger objektet i S3, men kvitteringen er tom. Både `recover` og `stage`
fejler så for det ID. Objektet skader ikke, fordi intet i kataloget peger på det, men nogen skal slette den tomme
kvittering og køre `stage` igen.

## Opgave 3: stage en upload

### Hvad `StageAsync` gør

1. `Reserve` opretter en tom kvittering. Findes den allerede, stopper vi her.
2. `UploadAsync` sender filen til S3 og får et version-ID tilbage.
3. Er version-ID'et tomt, `null` eller `None`, kaster vi `MissingVersionId`.
4. Vi beregner sha256 og antal newlines ud fra de samme bytes, som blev uploadet.
5. `WriteAsync` gemmer report-ID, bucket, key, version-ID og de to målinger i kvitteringen.
6. Vi returnerer version-ID'et.

### Hvorfor et tomt version-ID bliver afvist

Et tomt version-ID betyder typisk, at versionering ikke er slået til på bucketen. Så kan den næste PUT til samme key
overskrive filen, og kvitteringen kan ikke pege på præcis de bytes, vi uploadede.

Vi kaster derfor, før kvitteringen bliver skrevet. En udfyldt kvittering betyder, at uploaden er i orden og venter på
at blive published. En tom kvittering betyder, at vi ikke ved, hvordan uploaden gik, og at den skal undersøges før en
ny PUT. Det er samme besked, som et crash mellem upload og `WriteAsync` giver.

## Opgave 4: verificer en præcis version

### Hvad `VerifyAsync` gør

1. `FindAsync` slår rapporten op i kataloget. Fejler databasen, returnerer vi `CatalogueReadFailed`.
2. Findes rækken ikke, returnerer vi `UnknownReport`.
3. Ellers kalder vi `CheckAsync` med den gemte række.

`CheckAsync` henter præcis den bucket, key og version, som kataloget peger på. Den måler sha256 og antal newlines og
returnerer `Matches`, hvis begge passer, og ellers `Mismatch`. Kan filen ikke læses, laver den `ObjectStoreException`
om til `ReadFailed`.

### Hvorfor vi slår op i kataloget først

Kataloget bestemmer, hvilken version der hører til en rapport. Læste vi bare den nyeste fil på key'en, ville en senere
upload få en gammel rapport til at se forkert ud. Det er derfor, versionering er vigtig: to rapporter kan dele samme
key og stadig pege på hver sin fil.

### Exit-koder

CLI'en bruger enum-værdien som exit-kode: `Matches` = 0, `Mismatch` = 1, `UnknownReport` = 3,
`CatalogueReadFailed` = 4 og `ReadFailed` = 5. Kode 2 bruges ikke af enum'en, fordi `Program.cs` bruger den til
forkerte argumenter. Et script kan derfor se forskel på "filen er ændret" og "vi kunne ikke tjekke den".

### Konsekvens for `recover`

`recover` bruger også `CheckAsync`. Får den andet end `Matches`, kaster den enum-navnet, så stderr siger `ReadFailed`,
når S3 er nede, og `Mismatch`, når kvitteringen ikke passer til filen. Exit-koden er 1 i begge tilfælde.

## Opgave 5: publish og recover

### Hvad `RecoverAsync` gør

1. `ReadAsync` læser kvitteringen. Er den tom eller ugyldig, fejler den med `InvalidReceipt`.
2. `CheckAsync` henter den version, kvitteringen peger på, og tjekker sha256 og antal rækker.
3. Er resultatet ikke `Matches`, kaster vi enum-navnet, altså `Mismatch` eller `ReadFailed`.
4. `SaveAsync` skriver rækken i kataloget. Først når den committer, er rapporten published.
5. Vi returnerer version-ID'et fra kvitteringen.

`PublishAsync` kører `StageAsync` og returnerer derefter resultatet af `RecoverAsync`. En normal publish og en
recovery efter et afbrud går altså gennem samme kode.

### Hvorfor `recover` kan køres flere gange

`recover` laver aldrig en PUT. Den bruger det version-ID, der står i kvitteringen, så den kan ikke uploade en ny
version ved en fejl. `SaveAsync` accepterer en række, der er helt magen til den, der allerede ligger i kataloget, og
gør så ingenting. Er noget anderledes, for eksempel en anden `supersedes`, kaster den `ReportConflict`.

### Hvorfor `stage` findes som egen kommando

`stage` stopper, når kvitteringen er gemt. Så kan man efterligne et afbrud mellem upload og katalog og bagefter vise,
at `recover` gør arbejdet færdigt.

### Åbent punkt

Opgaven siger "Return Saved ID once the catalogue write succeeds". Vi returnerer version-ID'et. Det er ikke afklaret,
om sætningen i stedet betyder teksten `Saved` efterfulgt af report-ID'et.
