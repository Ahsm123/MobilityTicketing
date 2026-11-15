# Lecture 10 evidence

**Note: Formater output med pipe | python -m json.tool**

## Experiments

Use a new ID for each independent trial. Keep the same ID when inspecting or
retrying the same logical request. Save the full JSON beside these notes.

| Member state               | Action | Operation ID | Read/write settings                                       | Prediction      | Actual result / saved output |
|----------------------------|--------|--------------|-----------------------------------------------------------|-----------------|------------------------------|
| 3 running, primary: mongo1 | write  | BASE-MAJ     | `w: "majority"`, `wtimeout: 2000`, readPreference primary | 2 acks required | Acknowledged, 105 ms         |
| same                       | write  | BASE-ONE     | `w: 1`, `wtimeout: 2000`, readPreference primary          | 1 ack required  | Acknowledged, 90 ms          |
| same                       | read   | BASE-MAJ     | readPreference primary, readConcern local                 | exists          | Present, 84 ms               |
| same                       | read   | BASE-MAJ     | readPreference secondary, readConcern local               | exists          | Present, 80 ms               |
|                            |        |              |                                                           |                 |                              |
|                            |        |              |                                                           |                 |                              |
|                            |        |              |                                                           |                 |                              |
|                            |        |              |                                                           |                 |                              |
|                            |        |              |                                                           |                 |                              |
|                            |        |              |                                                           |                 |                              |
|                            |        |              |                                                           |                 |                              |
|                            |        |              |                                                           |                 |                              |
|                            |        |              |                                                           |                 |                              |
|                            |        |              |                                                           |                 |                              |
|                            |        |              |                                                           |                 |                              |

## Read-only verification

| Operation ID | Earlier write outcome | Verification result | What this establishes / leaves uncertain |
|--------------|-----------------------|---------------------|------------------------------------------|
|              |                       |                     |                                          |

## Application policy

| Operation       | Freshness/consistency needed | During an outage | What the user sees | After recovery | Reason / evidence |
|-----------------|------------------------------|------------------|--------------------|----------------|-------------------|
| Journey search  |                              |                  |                    |                |                   |
| Ticket purchase |                              |                  |                    |                |                   |
| Reporting       |                              |                  |                    |                |                   |

## AI second opinion

- Prompt and model:
- Suggested claim or experiment:
- My prediction:
- Actual output:
- Decision and remaining uncertainty:

# 1: Read the cancellation from another member

- primary: mongo1
- secondaries: mongo2, mongo3

## 1.1: Predict numbers of acks each write requires

- write BASE-MAJ majority: 2

````json
{
  "action": "write",
  "operationId": "BASE-MAJ",
  "startedAt": "2026-11-15T20:01:57.288Z",
  "readPreference": "primary",
  "readConcern": null,
  "writeConcern": {
    "w": "majority",
    "wtimeout": 2000
  },
  "retryWrites": false,
  "retryReads": false,
  "result": {
    "n": 1,
    "electionId": {
      "$oid": "7fffffff0000000000000001"
    },
    "opTime": {
      "ts": {
        "$timestamp": {
          "t": 1794772917,
          "i": 2
        }
      },
      "t": 1
    },
    "ok": 1,
    "$clusterTime": {
      "clusterTime": {
        "$timestamp": {
          "t": 1794772917,
          "i": 2
        }
      },
      "signature": {
        "hash": {
          "$binary": {
            "base64": "AAAAAAAAAAAAAAAAAAAAAAAAAAA=",
            "subType": "00"
          }
        },
        "keyId": 0
      }
    },
    "operationTime": {
      "$timestamp": {
        "t": 1794772917,
        "i": 2
      }
    }
  },
  "outcome": "Acknowledged",
  "finishedAt": "2026-11-15T20:01:57.393Z",
  "durationMs": 105
}
````

- write BASE-ONE 1: 1

````json
{
  "action": "write",
  "operationId": "BASE-ONE",
  "startedAt": "2026-11-15T20:04:34.794Z",
  "readPreference": "primary",
  "readConcern": null,
  "writeConcern": {
    "w": 1,
    "wtimeout": 2000
  },
  "retryWrites": false,
  "retryReads": false,
  "result": {
    "n": 1,
    "electionId": {
      "$oid": "7fffffff0000000000000001"
    },
    "opTime": {
      "ts": {
        "$timestamp": {
          "t": 1794773074,
          "i": 1
        }
      },
      "t": 1
    },
    "ok": 1,
    "$clusterTime": {
      "clusterTime": {
        "$timestamp": {
          "t": 1794773074,
          "i": 1
        }
      },
      "signature": {
        "hash": {
          "$binary": {
            "base64": "AAAAAAAAAAAAAAAAAAAAAAAAAAA=",
            "subType": "00"
          }
        },
        "keyId": 0
      }
    },
    "operationTime": {
      "$timestamp": {
        "t": 1794773074,
        "i": 1
      }
    }
  },
  "outcome": "Acknowledged",
  "finishedAt": "2026-11-15T20:04:34.884Z",
  "durationMs": 90
}
````

- Begge writes returnerer acks. Vi kan kun se at primary giver acks, men vi ved at den første bruger majority, så derfra
  ved vi at
  mindst en anden har acks også. I One ved vi kun at primary har.

- read BASE-MAJ primary

````json
{
  "action": "read",
  "operationId": "BASE-MAJ",
  "startedAt": "2026-11-15T20:12:27.351Z",
  "readPreference": "primary",
  "readConcern": "local",
  "writeConcern": null,
  "retryWrites": false,
  "retryReads": false,
  "actual": {
    "_id": "BASE-MAJ",
    "tripId": "T102",
    "status": "Cancelled",
    "sourceVersion": 2
  },
  "outcome": "Present",
  "finishedAt": "2026-11-15T20:12:27.435Z",
  "durationMs": 84
}
````

- read BASE-MAJ secondary

````json
{
  "action": "read",
  "operationId": "BASE-MAJ",
  "startedAt": "2026-11-15T20:13:56.942Z",
  "readPreference": "secondary",
  "readConcern": "local",
  "writeConcern": null,
  "retryWrites": false,
  "retryReads": false,
  "actual": {
    "_id": "BASE-MAJ",
    "tripId": "T102",
    "status": "Cancelled",
    "sourceVersion": 2
  },
  "outcome": "Present",
  "finishedAt": "2026-11-15T20:13:57.022Z",
  "durationMs": 80
}
````

- Vi kan konstatere, at både primary og mindst én secondary har modtaget BASE-MAJ write.
  vi kan stadig have en secondary som er stale.

- En secondary kan godt være stale en anden gang, fordi replikeringen er asynkron, og primary venter ikke på
  secondaries,
  medmindre write concern kræver det. 