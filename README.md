# TsvdChain

A Bitcoin-inspired blockchain built with .NET 10 and SignalR for peer-to-peer communication.

## Architecture

```
TsvdChain.Core     Domain layer — blocks, transactions, consensus, mining, mempool, crypto
TsvdChain.P2P      SignalR hub and peer connection management
TsvdChain.Api      REST API, node service, persistence
TsvdChain.Tests    xUnit tests for core logic
```

### Key Design Decisions

- **ECDSA P-256** signatures with hex-encoded public keys as addresses
- **AES-256-GCM** encrypted wallet (PBKDF2-derived key, persisted to `wallet.json`)
- **SHA-256** block hashing with Merkle tree transaction roots
- **Proof-of-Work** with configurable difficulty (default: 3 leading zeros)
- **Hardcoded consensus** — block reward (50, halving every 210k blocks) and difficulty are not configurable; changing them makes blocks invalid to other nodes
- **Computed balances** — no stored balance; always derived by scanning the chain (UTXO-style accounting)
- **Immutable records** — blocks and transactions are C# `record` types with computed hashes

## Quick Start

### Docker (recommended)

```bash
docker compose up --build
```

This starts two nodes:

| Node  | API            | Role                       |
|-------|----------------|----------------------------|
| node1 | localhost:5001 | Seed node (no peers)       |
| node2 | localhost:5002 | Connects to node1 via seed |

Blocks mined on either node automatically sync to the other via SignalR.

```bash
# Stop
docker compose down

# Stop and wipe all data
docker compose down -v
```

### Local Development

```bash
dotnet run --project TsvdChain.Api
```

Configure via `appsettings.json` or environment variables:

| Setting                       | Description                    | Default |
|-------------------------------|--------------------------------|---------|
| `Wallet:Password`             | Wallet encryption password     | —       |
| `SeedNodes:EnableSeedNodes`   | Connect to seed peers on start | `false` |
| `SeedNodes:Nodes:0`           | First seed node URL            | —       |

## API Endpoints

| Method | Endpoint               | Description                              |
|--------|------------------------|------------------------------------------|
| GET    | `/chain`               | Full blockchain                          |
| GET    | `/latest-block`        | Most recent block                        |
| POST   | `/mine`                | Mine a single block                      |
| POST   | `/miner/start`         | Start continuous mining                  |
| POST   | `/miner/stop`          | Stop continuous mining                   |
| GET    | `/address`             | This node's wallet address               |
| GET    | `/balance`             | This node's balance                      |
| GET    | `/balance/{address}`   | Balance for any address                  |
| POST   | `/tx/send`             | Sign and submit a transaction            |
| GET    | `/mempool`             | Pending transactions                     |
| GET    | `/peers`               | Connected peers                          |

### Example: Send Coins Between Nodes

```bash
# 1. Mine a block on node1 to earn 50 coins
curl -X POST http://localhost:5001/mine

# 2. Get node2's address
curl http://localhost:5002/address

# 3. Send 10 coins from node1 to node2
curl -X POST http://localhost:5001/tx/send \
  -H "Content-Type: application/json" \
  -d '{"to": "<node2-address>", "amount": 10}'

# 4. Mine to confirm the transaction
curl -X POST http://localhost:5001/mine

# 5. Check balances
curl http://localhost:5001/balance   # 90 (100 mined - 10 sent)
curl http://localhost:5002/balance   # 10
```

## P2P Protocol

Nodes communicate via SignalR (`/blockchainHub`):

- **SubmitBlock** — Relay a new block to all connected peers
- **SendChain** — Respond with the full chain (used for initial sync)
- **ReceiveChain** — Accept a full chain and replace if longer and valid
- **ReceiveBlock** — Accept a single block and append if valid

Seed node connections are configured via `SeedNodes` settings. Outbound connections auto-reconnect.

## Consensus Rules

| Rule              | Value                                    |
|-------------------|------------------------------------------|
| Block reward      | 50 coins (halves every 210,000 blocks)   |
| PoW difficulty    | 3 leading zeros                          |
| Coinbase `From`   | `"system"`                               |
| Hash algorithm    | SHA-256                                  |
| Signature         | ECDSA P-256                              |

## Tests

```bash
dotnet test
```

Covers: hash determinism, chain validation, tampering detection, ECDSA signing, coinbase rules, reward halving, and difficulty enforcement.

## Project Structure

```
TsvdChain.Core/
  Blockchain/     Block, Blockchain, Transaction, Consensus
  Crypto/         KeyPair (ECDSA), WalletStore (AES-GCM)
  Hashing/        Sha256Hasher, MerkleTree
  Mempool/        MempoolService
  Mining/         MinerService (PoW loop)

TsvdChain.P2P/
  BlockchainHub.cs          SignalR hub
  PeerConnectionService.cs  Peer tracking
  IBlockchainNodeService.cs Interface for hub → node calls

TsvdChain.Api/
  Program.cs                Endpoints and DI wiring
  BlockchainNodeService.cs  Node service, persistence, broadcasting
```
