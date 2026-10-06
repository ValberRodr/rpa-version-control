# Validação da entrega

Data: 05/10/2026

## Verificações realizadas neste ambiente

- Estrutura da solução e referências de projetos revisadas.
- Todos os arquivos XAML, `.csproj` e `app.manifest` são XML bem-formado.
- Correspondência entre XAML com `x:Class` e code-behind verificada.
- Delimitadores dos arquivos C# verificados por análise estática.
- Busca por senhas/chaves embutidas realizada; nenhuma credencial do fluxo descartado foi incluída.
- Regras de exclusão, locks de rede, manifests SHA-256, publicação atômica por arquivo, detecção de drift, versão-base, retry de publicação e rollback preservando histórico revisados.

## Limitação do ambiente de geração

O ambiente usado para gerar o pacote não possui `dotnet`, MSBuild nem Windows/WinUI. Portanto, o executável Windows não foi compilado aqui.

Em uma máquina Windows com .NET 8/WinUI preparado, execute:

```powershell
.\scripts\build-release.ps1
```

O script executa `restore`, testes do Core e `publish` self-contained para `win-x64` antes de gerar o ZIP de distribuição.
