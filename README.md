# DevInbox

App de bandeja do Windows 11 que monitora seus pull requests no GitHub e dispara notificações toast nativas. Clicar na notificação abre o PR no navegador (com deep-link para o comentário quando possível); tudo fica registrado num histórico local com estado lido/não lido.

## Notificações (v1)

| Evento | Como é detectado |
|---|---|
| 💬 Comentário novo no meu PR | GraphQL — comentários de issue e de review |
| 👀 Review solicitado para mim | GraphQL — busca `review-requested:@me` |
| ✅ Conversa minha resolvida | GraphQL — transição `isResolved` false→true |
| ✅/✖ Review recebido (aprovado/mudanças/comentado) | GraphQL — reviews com veredito |
| ❌ CI/checks falharam | GraphQL — `statusCheckRollup` transiciona para FAILURE/ERROR |
| ⚠ Conflito de merge | GraphQL — `mergeable` MERGEABLE→CONFLICTING |
| 📣 Menção a mim | REST Notifications API (`reason: mention`) |

O polling roda a cada 60s (configurável, mínimo 15s), custa ~6 pontos do rate limit GraphQL por ciclo (~8% do budget de 5.000/h) e usa `If-Modified-Since` na API REST (304 não consome rate limit).

## Requisitos

- Windows 11 (ou Windows 10 2004+)
- Para executar: .NET 10 Desktop Runtime — **instalado automaticamente** se você abrir o app pelo `Iniciar DevInbox.cmd` (distribuído junto do exe): ele verifica o runtime e, se faltar, instala silenciosamente via winget (fallback: instalador oficial da Microsoft em modo `/quiet`) antes de abrir o app. Quem já tem o runtime pode executar o `DevInbox.exe` direto.
- Para desenvolver: .NET 10 SDK
- Para autenticar (uma das opções):
  - **GitHub CLI** (recomendado): `winget install GitHub.cli` + `gh auth login`
  - **Token de acesso pessoal** (classic) com permissões `repo` e `notifications` — [criar token com permissões pré-marcadas](https://github.com/settings/tokens/new?scopes=repo,notifications&description=DevInbox). O token fica cifrado com DPAPI em `%APPDATA%\DevInbox\token.bin`.

O app tenta o gh CLI primeiro; sem ele, usa o token salvo; sem nenhum, abre a janela de configuração.

## Instalador

O instalador (Inno Setup) é a forma recomendada de distribuir: ele detecta se o **.NET 10 Desktop Runtime** está presente e, se faltar, **baixa o pacote oficial da Microsoft e o instala silenciosamente durante o setup**. Por padrão instala por usuário (sem UAC); o próprio diálogo permite escolher instalação por máquina.

```powershell
# gerar o instalador (exige Inno Setup: winget install JRSoftware.InnoSetup)
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" installer\DevInbox.iss
# saída: installer\output\DevInbox-Setup-<versão>.exe
# instalação silenciosa: DevInbox-Setup-x.y.z.exe /CURRENTUSER /VERYSILENT
```

Antes de compilar o instalador, rode o `dotnet publish` abaixo — o script empacota a pasta publish.

## Comandos

```powershell
dotnet build                          # compila
dotnet test                           # roda os testes (StateDiffer é o núcleo)
dotnet run --project src\DevInbox.App   # executa em modo dev

# Release: .exe único framework-dependent (~27 MB; exige .NET 10 Desktop Runtime)
dotnet publish src\DevInbox.App -c Release -r win-x64 --self-contained false `
  -p:PublishSingleFile=true

# Alternativa self-contained (~180 MB, zero pré-requisitos):
#   --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

## Interface

- **Bandeja**: badge vermelho com contagem de não lidas; menu Histórico / Configurações / Sobre / Sair.
- **Janela principal**: aba **Pendências de review** (conversas de review ainda abertas nos seus PRs — ao serem resolvidas saem da lista e o evento vai para o histórico; é a aba inicial, exceto quando vazia) e aba **Histórico** (todas as notificações, clique duplo abre o PR).
- **Configurações**: intervalo, aparência (5 temas trocáveis em runtime — GitHub Dark padrão, Darcula, VS Code, Solarized Light e Claro), liga/desliga por evento, iniciar com o Windows, som de notificação (on/off + dropdown com botão "Ouvir"), tempo do toast na tela (padrão ~7 s ou longa ~25 s — únicos valores que o Windows oferece), horário silencioso, e integrações (GitHub hoje; preparado para novas integrações no futuro).
- **Notificação**: clicar no corpo do toast abre o PR no navegador e marca como lida; o botão "Descartar" só dispensa o aviso.
- **Sons de notificação**: os arquivos ficam na pasta `audio\` na raiz do projeto (copiada para junto do exe no build/publish). Aceita `.wav` e `.mp3` — basta adicionar arquivos lá que eles aparecem no dropdown. Ao tocar, o app **não pausa** o áudio do sistema: ele apenas reduz temporariamente o volume das outras sessões pela metade (ducking via WASAPI) e toca o aviso por cima.

## Arquitetura

```
src\DevInbox.Core     # sem WPF — lógica testável (net10.0)
  Auth\                    # cadeia gh CLI → token pessoal (DPAPI)
  GitHub\                  # clientes GraphQL (eventos 1–6) e REST (menções)
  Polling\                 # PollingEngine + StateDiffer (diff puro snapshot × estado)
  Storage\                 # SQLite: pr_state, seen_items, thread_state, notifications
  Settings\                # settings.json com escrita atômica
src\DevInbox.App      # WPF: bandeja (H.NotifyIcon), toasts (Toolkit), temas plugáveis (paletas trocáveis em runtime)
tests\DevInbox.Core.Tests
```

Conceitos-chave:
- **Baseline silencioso** — o primeiro poll (e a primeira visão de cada PR) só registra estado, sem toasts.
- **Dedup** — `INSERT OR IGNORE` por `dedup_key` na tabela `notifications` é o único portão; sobrevive a restarts e polls sobrepostos.
- **Estados transitórios** — `mergeable: UNKNOWN` não sobrescreve o último estado definitivo, para não perder a transição de conflito.

## Arquivos em runtime

| Caminho | Conteúdo |
|---|---|
| `%LOCALAPPDATA%\DevInbox\state.db` | estado dos PRs + histórico (SQLite/WAL) |
| `%LOCALAPPDATA%\DevInbox\logs\` | logs Serilog (7 dias) |
| `%APPDATA%\DevInbox\settings.json` | configurações |
| `%APPDATA%\DevInbox\token.bin` | token de acesso pessoal cifrado (DPAPI), se configurado |

Na primeira execução após o rebrand, as pastas da era "GitHubChecker" são migradas automaticamente para os caminhos acima (histórico, configurações e dedup preservados).

## Backlog (fora da v1)

PR mergeado/fechado · novos commits em PR que reviso · PR pronto para merge (aprovado + checks verdes) · draft→ready · lembrete de PR parado · review solicitado para o time · filtros por repo/organização · snooze de notificação · multi-conta.
