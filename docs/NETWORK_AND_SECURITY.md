# Rede e segurança

## Estrutura

```
RPA-Version-Control\
  config\
    security.json
    change-sequence.json
  projects\
    <project-id>\
      project.json
      versions\
  changes\
    CHG-000001\
      change.json
      revisions\
  repositories\
    <project-id>.git\
  locks\
  audit\
```

## Identidade

O app registra:

- `DOMINIO\usuario`;
- nome amigável derivado do login;
- máquina;
- data/hora.

Não existe senha embutida.

## Importante: autorização real

Sem servidor, uma checagem dentro do executável não é uma barreira de segurança suficiente. A autorização real deve vir das **ACLs NTFS/SMB**.

Recomendação:

| Local | DEV | QA | Admin |
|---|---|---|---|
| config | leitura | leitura | modificar |
| projects | leitura | leitura | modificar |
| repositories | leitura | modificar | modificar |
| changes | criar/editar próprios | modificar | modificar |
| locks | modificar | modificar | modificar |
| audit | criar/ler | criar/ler | administrar |
| produção | sem escrita | modificar | modificar |

Use grupos de domínio sempre que possível.

O arquivo `security.json` controla o que a UI expõe. As ACLs controlam o que a credencial Windows realmente consegue fazer.

## Bootstrap

Se `security.json` não existir, o primeiro usuário que configurar uma raiz nova é incluído como Admin e QA. Em produção corporativa, prefira que Infra pré-crie a raiz e o `security.json` com ACL de Administração antes da distribuição.
