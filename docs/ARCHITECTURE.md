# Arquitetura

## Objetivo

Aplicativo Windows para versionamento de RPAs sem servidor de aplicação. O Git é interno e invisível ao usuário.

## Componentes

- **WinUI 3 / Windows App SDK**: UI, Mica, notificações.
- **H.NotifyIcon.WinUI**: ícone na bandeja.
- **LibGit2Sharp**: commits, tags, diff, histórico e repositórios bare.
- **JSON atômico + arquivos**: workflow e metadados compartilhados.
- **Pasta de rede**: fonte compartilhada.
- **Identidade Windows**: nome de DEV/QA e auditoria.

## Regra de ouro

A pasta oficial nunca é usada como repositório Git. Ela contém a versão operacional e pode conter logs.

O app mantém um repositório **bare** separado na pasta de versionamento:

```
\\rede\RPA-Version-Control\
  repositories\<project-id>.git
```

Somente arquivos controlados entram no Git.

## Fluxo DEV

1. Abre o projeto.
2. Seleciona a pasta desenvolvida.
3. O app cria uma cópia temporária local da última versão.
4. Aplica os arquivos candidatos filtrando regras de ignore.
5. Gera diff.
6. DEV preenche incidente, motivo, alteração e impacto.
7. O app grava snapshot imutável da revisão em `changes`.
8. QA recebe na fila/notificação.

## Fluxo QA

Checklist obrigatório:

1. Relação com incidente/solicitação.
2. Escopo restrito ao necessário.
3. Tratamento de erros/exceções.
4. Logs/rastreabilidade.
5. Sem alteração indevida/credencial/risco evidente.
6. Sustentação, e não retrofit.

Qualquer `Não` bloqueia a aprovação. `Não` exige comentário.

A pergunta 6 com `Não` classifica como `RetrofitRequired`.

## Publicação

A aprovação:

1. Adquire lock da CHG.
2. Adquire lock do projeto.
3. Confirma a versão-base.
4. Confirma que o HEAD do Git e o metadado do projeto estão sincronizados.
5. Detecta drift na pasta oficial.
6. Cria commit/tag da nova versão.
7. Atualiza somente arquivos controlados da pasta oficial.
8. Verifica hash pós-publicação.
9. Persiste o registro de versão.

Falha entre commit e produção fica como `PublishFailed` e pode ser retomada sem criar uma versão duplicada.

## Rollback

Rollback nunca apaga histórico.

Restaurar `v3` quando a atual é `v7` cria `v8` com conteúdo equivalente à `v3`.

## Logs

Logs continuam na pasta oficial. As regras de ignore impedem que sejam:

- copiados para snapshots;
- comparados;
- versionados;
- removidos durante deploy;
- removidos durante rollback.

## Concorrência

Locks são arquivos abertos com `FileShare.None` na pasta de rede. Há locks independentes para:

- geração de sequência `CHG`;
- revisão da CHG;
- publicação por projeto.

## Drift

Antes de publicar, o hash da produção controlada é comparado ao manifesto da última versão aprovada. Se divergir, a publicação é bloqueada.
