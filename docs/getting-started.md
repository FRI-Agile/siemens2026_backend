# Getting Started

Step by step from a fresh clone to the whole application running locally: the backend (API with PostgreSQL, Azurite, Mailpit and ClamAV) under the Aspire AppHost, and the frontend on the Vite dev server. No secrets and no Azure access are needed.

The details behind each step are in [Development Architecture §3](architecture/development-architecture.md#3-local-development), [Aspire Development Stack](architecture/aspire-dev-stack.md) and [Getting Started with the Frontend](app/getting-started.md). This page only puts them in order.

## 1. Prerequisites

You install three things yourself; [mise](https://mise.jdx.dev/) installs the rest from `mise.toml` in the repository root.

| Tool | Version | Installed by | Needed for |
| ---- | ------- | ------------ | ---------- |
| Git | any | you | Cloning; on Windows, Git for Windows also provides the Bash the `Justfile` runs its recipes in |
| Docker | Docker Desktop, running | you | The containers the AppHost starts, and the integration tests (Testcontainers) |
| mise | recent | you | Installing the tools below and running the setup task |
| .NET SDK | 10 | mise | The API, the AppHost and the tests |
| Node.js with npm | 24 | mise | The frontend, and the Playwright browser install for the end-to-end tests |
| just | latest | mise | The commands in the `Justfile` (optional: every recipe is a plain command) |
| Aspire CLI | 13.6.1, the AppHost's SDK version | mise | `aspire run` (optional: `dotnet run --project AppHost` does the same without it) |

The first setup and the first start download a lot: the .NET SDK, the npm packages and the container images, of which ClamAV and its signatures are the largest.

### 1.1 Docker

- **Windows and macOS:** install [Docker Desktop](https://docs.docker.com/desktop/) and start it.
- **Linux:** install [Docker Engine](https://docs.docker.com/engine/install/) and add your user to the `docker` group (log out and in afterwards), or install [Podman](https://podman.io/docs/installation).

Check that it runs:

```bash
docker info
```

It must print a `Server:` section without errors. With Podman, run `podman info` instead.

### 1.2 mise

mise downloads the right versions of .NET, Node.js, `just` and the Aspire CLI for this project, so you do not install them yourself. Setting it up takes two commands, run once: the first installs mise, the second lets your terminal find the tools mise downloads. Follow the steps for your system.

#### Windows

In PowerShell:

1. Install mise:

   ```powershell
   winget install jdx.mise
   ```

2. Let PowerShell find the tools (two commands):

   ```powershell
   if (-not (Test-Path $PROFILE)) { New-Item -ItemType File -Path $PROFILE -Force | Out-Null }
   ```

   ```powershell
   Add-Content $PROFILE '(&mise activate pwsh) | Out-String | Invoke-Expression'
   ```

#### macOS

In Terminal, with [Homebrew](https://brew.sh):

1. Install mise:

   ```bash
   brew install mise
   ```

2. Let the terminal find the tools:

   ```bash
   echo 'eval "$(mise activate zsh)"' >> ~/.zshrc
   ```

#### Linux

In a terminal:

1. Install mise:

   ```bash
   curl https://mise.run | sh
   ```

2. Let the terminal find the tools:

   ```bash
   echo 'eval "$(~/.local/bin/mise activate bash)"' >> ~/.bashrc
   ```

   This is for Bash, the default on most distributions. For another shell, see [Activate mise](https://mise.jdx.dev/getting-started.html#activate-mise).

#### Check it

Close the terminal and open a new one, then run:

```bash
mise --version
```

It prints a version number. If it does not, see §9.

## 2. Set up the repository (once)

1. Clone the repository and change into it:

   ```bash
   git clone https://github.com/lfalat/siemens2026_backend.git healthyhires
   ```

   ```bash
   cd healthyhires
   ```

2. Allow mise to read the repository's `mise.toml` (mise asks once per configuration file):

   ```bash
   mise trust
   ```

3. Install the tools and everything the project needs:

   ```bash
   mise run setup
   ```

`mise run setup` installs the tools from §1 that are missing, then runs two tasks:

| Task | What it does |
| ---- | ------------ |
| `setup:backend` | `dotnet restore healthyhires.slnx`, and creates and trusts the HTTPS development certificate the AppHost's default launch profile needs |
| `setup:frontend` | `npm ci` in `App/`, and creates `App/public/config.json` from `config.example.json` if it does not exist yet |

Run it again after pulling changes to `package-lock.json` or the package versions; it is quick when nothing changed. The container images are not part of it: the AppHost pulls them on its first start (§3).

**HTTPS certificate on Linux.** .NET cannot add the certificate to every Linux trust store, so the setup may print `There was an error trusting the HTTPS developer certificate` and continue. The browser then only warns on `https://localhost`. Accept the warning, or start the AppHost with the `http` profile (§3), or follow the [.NET dev-certs instructions](https://aka.ms/dev-certs-trust) for your distribution.

## 3. Start the backend

In a first terminal, from the repository root:

```bash
just aspire-up
```

This runs `dotnet run --project AppHost`; `aspire run` is equivalent. To avoid HTTPS altogether, run `dotnet run --project AppHost --launch-profile http` instead.

What happens:

1. The AppHost builds the API and opens the **Aspire dashboard** in the browser, logged in with a one-time token. If the browser asks for a token, open the login URL printed in the terminal.
2. It starts PostgreSQL, Azurite, Mailpit and ClamAV. The first start pulls their images, which takes a few minutes.
3. The API starts once PostgreSQL, Blob Storage and Mailpit are healthy. ClamAV may stay unhealthy for up to a minute while it loads its signatures; the API runs without it.

The backend is ready when `apiservice` shows **Running** and **Healthy** on the dashboard's Resources page. The database starts empty on every run and is filled from `configs/postgres/init/`, development data included ([aspire-dev-stack.md §6](architecture/aspire-dev-stack.md#6-lifecycle--nothing-persists)).

## 4. Start the frontend

In a second terminal, from the repository root:

```bash
just app
```

This runs `npm run dev --prefix App`. Open `http://localhost:5173`. The dev server proxies `/api` and `/hubs` to the API on `http://localhost:5180`, so the frontend needs no configuration. The backend and the frontend are independent: either can be restarted without the other.

## 5. Where everything is

| What | URL |
| ---- | --- |
| Application | `http://localhost:5173` |
| Aspire dashboard (logs, traces, resource health) | `https://localhost:17180` (`http://localhost:15180` with the `http` profile) |
| API | `http://localhost:5180` |
| API reference (Scalar) | `http://localhost:5180/scalar` |
| E-mail inbox (Mailpit) | `http://localhost:8025` |
| PostgreSQL | `localhost:5432`, user `healthyhires`, password `healthyhires-dev`, database `healthyhires` |
| Blob Storage (Azurite) | `http://localhost:10000`, the well-known `devstoreaccount1` account |

All ports are fixed; the full list is in [aspire-dev-stack.md §1](architecture/aspire-dev-stack.md#1-resources).

## 6. Stop

Press **Ctrl+C** in both terminals. The AppHost removes its containers; nothing but ClamAV's signature volume is kept ([aspire-dev-stack.md §6](architecture/aspire-dev-stack.md#6-lifecycle--nothing-persists)).

## 7. Next steps (optional)

| Task | How |
| ---- | --- |
| Run the backend tests | `just test`; needs Docker or Podman running ([development-architecture.md §4](architecture/development-architecture.md#4-tests)) |
| Sign in to the staff pages | Works against the real Entra tenant with an account that has an app role ([iam.md §8](architecture/iam.md#8-local-development)). Creating staff users also needs `dotnet user-secrets set Entra:UserDomain <verified-domain> --project AppHost` |
| Run the end-to-end tests | `just e2e-setup` once, then `just e2e` with the backend and frontend running ([e2e.md](testing/e2e.md)) |
| See the production headers and CSP | `just app-build`, then `just edge`, and open `http://localhost:8088` ([development-architecture.md §3.3](architecture/development-architecture.md#33-production-like-edge-optional)) |
| List every command | `just` |

## 8. Without mise

mise is a convenience, not a requirement. Install the .NET 10 SDK from [dot.net](https://dot.net), Node.js 24 from [nodejs.org](https://nodejs.org) and optionally [just](https://github.com/casey/just#installation) yourself, then run the setup steps by hand from the repository root:

```bash
dotnet restore healthyhires.slnx
```

```bash
dotnet dev-certs https --trust
```

```bash
npm ci --prefix App
```

```bash
cp App/config.example.json App/public/config.json
```

Start the backend with `dotnet run --project AppHost` and the frontend with `npm run dev --prefix App`, as in §3 and §4.

## 9. Troubleshooting

| Symptom | Cause | Fix |
| ------- | ----- | --- |
| `mise` reports `Config files … are not trusted` | `mise trust` was not run in this clone | Run `mise trust` |
| `mise` is not found | The terminal was opened before mise was set up | Open a new terminal |
| `just`, `dotnet` or `node` is not found, or has the wrong version | Step 2 of §1.2 was skipped, or the terminal was opened before it | Do step 2 and open a new terminal; in the meantime, put `mise exec --` in front of the command |
| PowerShell says running scripts is disabled when it starts | Windows blocks the profile script that step 2 of §1.2 writes | Run `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned` once, then open a new PowerShell |
| The AppHost reports that no container runtime was found, or that it is unhealthy | Docker or Podman is not running | Start it and check with `docker info` (§1.1) |
| `permission denied … /var/run/docker.sock` on Linux | Your user is not in the `docker` group | `sudo usermod -aG docker $USER`, then log out and in |
| A resource fails with "address already in use" | Its fixed port is taken, often by a second AppHost or a `just edge` left running | Stop the other process |
| The browser warns about the certificate on `https://localhost:17180` | The development certificate is not trusted (§2) | Accept the warning, or use the `http` profile |
| `npm run dev` fails with `Port 5173 is already in use` | Another dev server is running | Stop it; the port is fixed because it is the registered sign-in redirect URI |
| The frontend's `/api` calls answer 502 | The API is not running, or not healthy yet | Start the backend (§3) and wait for `apiservice` to be healthy |

More: [Aspire Development Stack §7](architecture/aspire-dev-stack.md#7-troubleshooting) and [Getting Started with the Frontend §7](app/getting-started.md#7-troubleshooting).
