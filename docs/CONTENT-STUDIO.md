# Content Studio — guia de uso

Ferramenta de desenvolvimento para cadastrar o conteúdo do jogo: times, jogadores,
treinadores, competições, estádios, contratos. **Não faz parte do jogo** e não é distribuída
com ele — é um utilitário local que edita os arquivos em `content/dev/`.

Este é o único documento do repositório em português; o resto (código, comentários,
`README.md`, `ARCHITECTURE.md`) está em inglês.

## Pré-requisito

Apenas o **.NET 8 SDK**. A ferramenta não abre o Godot nem depende dele.

## Rodar

```bash
dotnet run --project tools/SoccerSim.ContentStudio
```

Abra <http://127.0.0.1:5099> no navegador. O caminho da pasta de conteúdo aparece no
cabeçalho — confirme que é o `content/dev` do seu clone.

Pode rodar de qualquer diretório: caminhos relativos são resolvidos a partir da raiz do
repositório (a ferramenta sobe até achar `SoccerDreamGame.sln`), não do diretório atual. Para
apontar para outra pasta de propósito:

```bash
dotnet run --project tools/SoccerSim.ContentStudio -- --content /caminho/para/content/dev
```

Se a pasta não tiver um `manifest.json`, a ferramenta se recusa a abrir em vez de começar com
um pacote vazio — que seria gravado por cima no primeiro clique.

Para encerrar, `Ctrl+C` no terminal. **Toda edição é gravada em disco na hora** — não existe
botão de salvar e não há nada para perder ao fechar.

## A tela

- **Esquerda** — as dez categorias editáveis, nesta ordem: `nations`, `stadiums`,
  `competitions`, `traits`, `leagues`, `teams`, `players`, `coaches`, `contracts`,
  `housing_items`. O número ao lado é a quantidade de registros.

  Temporadas e tabela de jogos (`seasons`, `fixtures`, em `world.json`) fazem parte do pacote
  mas **não são editáveis pela grade** — hoje só por edição manual do JSON ou pelo gerador de
  calendário.
- **Centro** — a planilha da categoria escolhida. Clique numa célula para editar; sair da
  célula grava. **Add row** cria um registro, **Paste / CSV…** importa em massa, **Export
  CSV** baixa a categoria inteira.
- **Direita** — o painel de validação, atualizado a cada alteração.
- **Busca** — filtra as linhas da categoria atual por qualquer campo de texto.

### `id` e `key`

Todo registro tem os dois, e eles servem a propósitos diferentes:

- **`id`** — número estável. É o que os saves guardam como chave estrangeira. Não mude o `id`
  de algo que já existe: você repontaria partidas e classificação para outra linha.
- **`key`** — texto (`alpha-fc`, `joao-silva`). É o que os outros arquivos JSON usam para se
  referenciar. É o que aparece nos campos `teamKey`, `nationKey`, `stadiumKey` etc.

A ferramenta preenche os dois ao criar uma linha. Trocar uma `key` é seguro desde que você
atualize quem aponta para ela — a validação acusa referência quebrada.

## Cadastro em massa (colar de planilha)

**Paste / CSV…** aceita colar direto do Excel/Google Sheets (separado por tabulação) ou CSV.

1. A **primeira linha é o cabeçalho** e precisa ter os nomes dos campos, iguais aos das
   colunas da grade.
2. Campos aninhados usam ponto: `attributes.pace`, `attributes.stamina`.
3. Listas usam barra vertical: `traitKeys` com valor `clutch|hothead`.
4. Clique em **Check** primeiro: ele mostra o que seria importado e o que falharia, **sem
   gravar nada**. Só então **Import**.

Exemplo mínimo para jogadores:

```
key	firstName	lastName	teamKey	primaryRole	attributes.pace	attributes.shooting
r-santos	Rafael	Santos	alpha-fc	Forward	78	81
l-moreira	Lucas	Moreira	alpha-fc	Midfielder	69	64
```

Colunas ausentes ficam com o padrão do campo. Uma `key` que já existe **atualiza** a linha
existente em vez de duplicar.

## Erros e avisos

O painel de validação separa os dois, e a diferença importa:

- **Erro** (vermelho) — o conteúdo está inconsistente e o jogo rejeitaria. Referência
  quebrada, `id` duplicado, campo obrigatório vazio. Erro **barra o build**.
- **Aviso** (amarelo) — legal mas suspeito. Elenco com 5 jogadores, liga com número ímpar de
  times, jogador sem contrato. Aviso **não barra nada**.

A validação nunca impede a digitação: você pode deixar o conteúdo quebrado enquanto trabalha
e arrumar depois. Ela só é obrigatória no build.

O botão **Verify build** importa o pacote inteiro num banco SQLite de verdade
(`build/content/content.db`) para provar que ele carrega sem violar nenhuma constraint. É uma
**verificação** — o jogo não lê esse arquivo, ele lê os JSON (veja a próxima seção). Use antes
de commitar.

## Ver o conteúdo no jogo

Este é o ponto que mais confunde: **editar na ferramenta não muda o jogo sozinho**. Os JSON
são embutidos na `game.dll` em tempo de compilação (`game/game.csproj`), e o jogo importa esse
pacote para o save na primeira execução. Então:

**1. Editar** na ferramenta → grava `content/dev/*.json` na hora.

**2. Recompilar o jogo** — sem isto a `game.dll` ainda carrega o conteúdo antigo:

```bash
dotnet build game/game.csproj
```

(Abrir o projeto no Godot também recompila; rodar o comando antes só torna o erro de
compilação mais fácil de ler.)

**3. Rodar** — abrir `game/` no **Godot 4.6 (.NET/Mono)** e dar Run.

O save antigo guarda o conteúdo anterior, e os dois não podem ser reconciliados: os `id`s que
partidas e classificação apontam seriam renumerados. Em build de desenvolvimento o jogo
**recria o save automaticamente** e escreve um aviso no console dizendo isso. A carreira
anterior se perde — é o comportamento certo enquanto o mundo ainda está sendo montado.

**Se você já tinha partidas jogadas**, o jogo se recusa a apagar e falha o boot com uma
mensagem nomeando o arquivo. Aí a decisão é sua: apague o save à mão para começar de novo.

O nome da pasta vem de `config/name` em `game/project.godot`, hoje `Soccer Dream Game`:

| Sistema | Caminho do save |
| --- | --- |
| Linux | `~/.local/share/godot/app_userdata/Soccer Dream Game/save.db` |
| macOS | `~/Library/Application Support/Godot/app_userdata/Soccer Dream Game/save.db` |
| Windows | `%APPDATA%\Godot\app_userdata\Soccer Dream Game\save.db` |

Apague também os vizinhos `save.db-wal` e `save.db-shm` se existirem. No Godot, **Project →
Open User Data Folder** abre essa pasta direto.

## Comandos headless

Mesmas operações sem navegador, para CI ou para rodar rápido:

```bash
dotnet run --project tools/SoccerSim.ContentCli -- validate   # sai com código != 0 em qualquer erro
dotnet run --project tools/SoccerSim.ContentCli -- build      # -> build/content/content.db
dotnet run --project tools/SoccerSim.ContentCli -- stats      # contagem por categoria
dotnet run --project tools/SoccerSim.ContentCli -- reexport   # regrava os JSON no formato canônico
```

`reexport` é útil depois de editar um JSON à mão: normaliza formatação e ordem dos campos, o
que deixa o diff do git limpo.

## Por que JSON e não SQL

`content/dev/*.json` é a **fonte da verdade**, versionada no git. Isso é o que permite revisar
uma mudança de conteúdo em pull request como texto legível, e resolver conflito de merge entre
duas pessoas cadastrando times diferentes.

O validador é **o mesmo código** que o jogo roda ao importar
(`SoccerSim.Content/Validation`), então a ferramenta não consegue aprovar conteúdo que o jogo
recusaria.
