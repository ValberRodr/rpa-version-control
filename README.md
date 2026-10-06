# RPA Version Control

Aplicativo Windows corporativo para versionamento de scripts/RPAs em pasta de rede, sem GitHub/GitLab e sem servidor de aplicação.

## O que está implementado

- Cadastro de projetos apontando para a pasta oficial.
- Baseline automático `v1`.
- Git embutido via LibGit2Sharp; usuário não precisa instalar ou conhecer Git.
- Regras de exclusão para `logs/`, `*.log`, caches, temporários etc.
- Prévia obrigatória do diff antes do DEV submeter.
- Comentários obrigatórios do DEV: incidente/solicitação, motivo, alteração e impacto.
- CHG sequencial e revisões preservadas.
- Fila de QA.
- Checklist obrigatório de 6 perguntas.
- Qualquer resposta `Não` bloqueia aprovação e exige comentário.
- Pergunta 6 identifica sustentação x retrofit.
- DEV visualiza retorno do QA e reenvia a mesma CHG.
- Aprovação cria nova versão, commit e tag.
- Histórico com DEV, QA e CHG.
- Comparação de qualquer versão com a atual.
- Rollback gera uma nova versão; nunca apaga histórico.
- Detecção de drift em produção.
- Proteção contra versão-base desatualizada.
- Locks de concorrência por CHG/projeto.
- Recuperação de publicação interrompida.
- Usuário identificado por `DOMINIO\usuario`.
- Perfis QA/Admin por usuário ou grupo Windows.
- Bandeja do Windows.
- Notificações nativas.
- Inicialização opcional com Windows.
- Dados compartilhados em pasta de rede.
- Nenhum SQLite compartilhado.

## Stack

- .NET 8
- WinUI 3 / Windows App SDK 2.4
- LibGit2Sharp 0.32
- CommunityToolkit.Mvvm
- H.NotifyIcon.WinUI

## Requisitos de build

Windows 10 1809+ ou Windows 11.

Instale:

1. Visual Studio 2022 com **Desktop development with .NET** e ferramentas WinUI/Windows App SDK, ou .NET 8 SDK em um Windows preparado para WinUI.
2. PowerShell 7 ou Windows PowerShell para o script de release.

## Build

Na raiz:

```powershell
.\scripts\build-release.ps1
```

Saída:

```text
artifacts\win-x64\
artifacts\RpaVersionControl-win-x64.zip
```

O publish é self-contained. O usuário abre `RpaVersionControl.App.exe`.

## Instalação corporativa via PowerShell

Todo push em `main` publica automaticamente um build no GitHub Actions e atualiza a
release `latest` do repositório. Para instalar ou atualizar em qualquer máquina
Windows, sem precisar abrir o navegador:

```powershell
irm https://raw.githubusercontent.com/ValberRodr/rpa-version-control/main/scripts/install.ps1 | iex
```

Para configurar a pasta compartilhada e criar atalho já na instalação, baixe o
script primeiro (comandos `irm | iex` não aceitam parâmetros nomeados):

```powershell
iwr https://raw.githubusercontent.com/ValberRodr/rpa-version-control/main/scripts/install.ps1 -OutFile install.ps1
.\install.ps1 -SharedRoot "\\servidor\pasta\RPA-Version-Control" -Shortcut -Launch
```

O script baixa `RpaVersionControl-win-x64.zip` da release mais recente, extrai em
`%LOCALAPPDATA%\RpaVersionControl` (preservando um `shared-root.txt` já existente, a
menos que `-SharedRoot` seja informado) e, opcionalmente, cria atalho e abre o app.
Rodar o mesmo comando de novo atualiza a instalação para o build mais recente.

## Configuração corporativa

Opção mais simples para distribuição: coloque `shared-root.txt` ao lado do executável:

```text
\\servidor\pasta\RPA-Version-Control
```

Alternativas:

- variável de ambiente `RPA_VERSION_CONTROL_ROOT`;
- selecionar a pasta em **Configurações**.

Prioridade: variável de ambiente → `shared-root.txt` → configuração local.

## Primeiro uso

1. Configure a raiz compartilhada.
2. O primeiro usuário vira Admin/QA quando não existe `security.json`.
3. Em **Configurações**, cadastre usuários/grupos de QA.
4. Cadastre um projeto e selecione sua pasta oficial.
5. O app cria a baseline `v1`.
6. DEV usa **Enviar nova versão**.

## Segurança

Leia `docs/NETWORK_AND_SECURITY.md`.

Sem servidor, o executável não deve ser tratado como barreira de autorização. Use permissões NTFS/SMB para impedir que DEV escreva em produção ou nos repositórios oficiais.

## Observação sobre logs

O app nunca remove arquivos ignorados. Uma publicação ou rollback toca apenas nos arquivos controlados pela versão anterior.

## Validação

Os testes do core ficam em:

```text
tests\RpaVersionControl.Core.Tests
```

Este pacote foi preparado em ambiente Linux sem SDK .NET/WinUI disponível; por isso o executável Windows não foi compilado aqui. O script de release executa restore, testes e publish em Windows.
