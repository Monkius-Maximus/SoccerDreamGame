# Terra Paralela — Geração de Identidade de Clube (Club Identity) — **v2**

> Companion doc do GDD. Cross-ref: **Club & World Data** · **Gerador de Escudos** (`CrestGenerator`) · **Tactics → Behavior** (`TeamTactics`) · **Prestígio de Competição** (bandas B1–B6) · GDD §5 (LOD).
> Convenção: prosa PT-BR, **identificadores em inglês**. Determinismo obrigatório (SplitMix64, stream isolado).
>
> **Escopo:** como uma âncora real vira um clube fictício completo, coerente e deslicenciado.
>
> **v2 fecha as sete decisões em aberto da v1** e adiciona quatro peças que faltavam para o módulo ser implementável: política de proveniência de campo (§5), seletor determinístico de regra de nomenclatura (§6.1), derivação do uniforme reserva (§7.2) e schema do `deviationAudit` (§3.1). Nada aqui reabre `D-01`…`D-09`.

---

## 1. Papel do sistema e fronteira

O `ClubIdentityGenerator` é uma ferramenta de **autoria de conteúdo (pipeline offline)**, não um sistema de runtime. Roda no editor/ferramenta de mundo, produz um `ClubIdentity` serializado, e o jogo **apenas lê** esse dado.

Consequência direta (*separation of concerns*): nenhuma regra de paródia, nenhuma métrica fonética e nenhuma tabela de desvio existe no build do jogo. O que chega ao jogador é dado inerte.

| | |
| :--- | :--- |
| **Entrada** | `ReferenceAnchor` (clube real + cidade real) — semente conceitual, nunca persistida no build. |
| **Saída** | `ClubIdentity` (JSON/`Resource`), consumido por `CrestGenerator`, `KitAssembler`, entidade `Stadium`, `TeamTactics` default. |
| **LOD** | Irrelevante. Roda zero vezes em runtime, em qualquer tier. |

**Regra de higiene de IP (não-negociável).** A `ReferenceAnchor` e o `deviationAudit` ficam no repositório de autoria. Não são empacotados. O build final não contém nenhum nome real de clube.

---

## 2. Convenção de chaves — **TRAVADA**

O template original da feature veio com chaves JSON em PT-BR (`metadados_identificacao`, `cor_primaria_hex`…). Isso viola a convenção do projeto e criaria um caminho de nomenclatura paralelo à base C#. **Decisão travada: chaves em inglês, valores e prosa em PT-BR.** O template PT-BR fica descontinuado — *one way*.

| Template original | Campo canônico |
| :--- | :--- |
| `metadados_identificacao` | `identity` |
| `geografia_e_alinhamento_narrativo` | `geography` |
| `brasao_e_heraldica` | `crest` |
| `identidade_visual_e_cores` | `palette` |
| `kit_de_jogadores` | `kits` |
| `estadio_e_infraestrutura` | `stadium` |
| `configuracoes_ia_equipe` | `aiProfile` |
| `padrao_de_desvio_aplicado` | `deviationAudit` (**não empacotado**) |

---

## 3. Contrato de dados (`ClubIdentity`)

```json
{
  "clubId": "string",
  "identity": {
    "officialName": "string",
    "shortName": "string",
    "displayCode": "AAA",
    "nickname": "string",
    "foundingYear": 0
  },
  "geography": {
    "cityName": "string",
    "countryId": "string",
    "districtArchetype": "DistrictArchetype"
  },
  "crest": {
    "shieldShape": "ShieldShape",
    "centralCharge": "string",
    "colors": ["#RRGGBB"],
    "motto": "string"
  },
  "palette": {
    "primary": "#RRGGBB",
    "secondary": "#RRGGBB",
    "tertiary": "#RRGGBB",
    "typographyStyle": "TypographyStyle"
  },
  "kits": {
    "collarStyle": "CollarStyle",
    "fitStyle": "FitStyle",
    "home": { "fabricPattern": "FabricPattern", "shirt": "#RRGGBB", "shorts": "#RRGGBB", "socks": "#RRGGBB" },
    "away": { "fabricPattern": "FabricPattern", "shirt": "#RRGGBB", "shorts": "#RRGGBB", "socks": "#RRGGBB" }
  },
  "stadium": {
    "name": "string",
    "capacity": 0,
    "atmosphereArchetype": "AtmosphereArchetype",
    "pitchSurface": "PitchSurface"
  },
  "aiProfile": {
    "defaultTacticalStyle": "TacticalStyle",
    "derbyRivalClubId": "string | null"
  }
}
```

**Notas de contrato:**

- `derbyRivalClubId` é **referência por id**, nunca nome literal — **travado** (§9.3). O template pedia "nome fictício recomendado do rival"; isso duplicaria a fonte de verdade.
- `crest.colors` é subconjunto derivado de `palette`, não paleta independente. Restrição: 2–3 cores.
- `clubId` é atribuído pelo registro de mundo **antes** da geração; o gerador o consome, não o inventa (é insumo do stream determinístico, §8).

### 3.1 `deviationAudit` — schema (repositório de autoria, **não empacotado**)

Faltava na v1. É o registro de revisão de IP; sem ele o lote da §12.5 não é auditável.

```json
{
  "clubId": "string",
  "anchorClubName": "string",
  "anchorCityName": "string",
  "namingRule": "NamingRule",
  "namingRuleReason": "string",
  "phoneticSimilarity": 0.0,
  "crestSubstitution": {
    "originalCharge": "string",
    "substituteCharge": "string",
    "sourceCitation": "string"
  },
  "chromaticPolicy": "Preserved",
  "anchorFactsVerified": true,
  "reviewedBy": "string",
  "reviewDate": "YYYY-MM-DD"
}
```

- `phoneticSimilarity` só é preenchido quando `namingRule == Phonetic`; nos demais casos é `null`.
- `sourceCitation` é **obrigatório** em toda `HeraldicSubstitution`: o símbolo substituto precisa ser verificável na cidade real, e a verificação precisa ser rastreável. Sem citação, o registro não passa na revisão.
- `anchorFactsVerified` é o gate da §11: nenhum registro entra no lote com `false`.

---

## 4. Vocabulários canônicos (enums)

Confirmados em sessão. Identificador em inglês, rótulo de UI em PT-BR. **Todo enum é fechado; valor fora da faixa lança erro (fail-fast).**

### `DistrictArchetype`
`WorkingClass` (Operário/Industrial) · `Docklands` (Portuário) · `HistoricCenter` (Centro Histórico) · `Affluent` (Residencial Nobre) · `University` (Universitário) · `Outskirts` (Periférico) · `Coastal` (Litorâneo)

### `ShieldShape` — camada 1 do `CrestGenerator`
`Heater` (Clássico) · `Round` (Redondo) · `Oval` · `Square` (Quadrado) · `Ogival`

### `AtmosphereArchetype`
`Cauldron` (Caldeirão) · `Traditional` (Tradicional/Familiar) · `Corporate` (Corporativo/Moderno) · `Hostile` (Hostil) · `Apathetic` (Apático/Frio)

### `PitchSurface` — entra na física de rolagem/quique
`Pristine` (Impecável, rápido) · `Heavy` (Pesado/Irregular, lento) · `Synthetic` (Sintético) · `Worn` (Castigado)

### `TacticalStyle` — preset de `TeamTactics` da IA
`Possession` · `Counter` · `HighPress` · `LowBlock` · `Controlled` · `Direct`

### `TypographyStyle`
`ModernSans` · `ClassicSerif` · `Blackletter` (Gótica) · `Geometric` · `Stencil`

### `CollarStyle`
`VNeck` · `Polo` · `Crew` (Careca) · `Grandad` (Padre) · `ButtonedSport`

### `FitStyle`
`Slim` · `Regular` · `Retro` (Retrô/Largo)

### `FabricPattern`
`Solid` · `VerticalStripes` · `HorizontalStripes` · `Sash` · `ContrastSleeves` · `Pinstripes` · `Checks`

### `NamingRule` (novo em v2 — usado pelo seletor da §6.1)
`Toponymic` · `RegionalCode` · `EpithetLift` · `Phonetic`

> **Fronteira arquitetura × conteúdo.** Os enums são *arquitetura* — cada valor tem consumidor mecânico e adicionar um valor exige código. `centralCharge` e `motto` são *conteúdo*: crescem livremente, sem tocar em código.

---

## 5. Política de proveniência de campo — **NOVO, travado**

Esta era a lacuna estrutural da v1: o doc definia *o que* cada campo é, mas não *de onde vem o valor*. Sem isso, a implementação teria de escolher entre sortear tudo (produz clubes incoerentes — atmosfera `Corporate` num clube operário de liga periférica com gramado `Pristine`) ou autorar tudo à mão (não escala para ~32 ligas).

**Regra travada:** *campo com consumidor mecânico ou com papel de reconhecimento é **derivado**; campo puramente decorativo é **amostrado**.*

| Campo | Proveniência | Justificativa |
| :--- | :--- | :--- |
| `identity.officialName` / `shortName` / `nickname` | Derivado da âncora via `NamingRule` (§6) | É o eixo de paródia |
| `identity.displayCode` | Derivado de `officialName` + registro global (§9.1) | Unicidade é restrição de sistema |
| `identity.foundingYear` | Derivado da âncora (§9.2) | Demografia factual; ordena narrativa de tradição |
| `geography.cityName` / `countryId` | Da âncora, **preservados** | O mundo tem nações e cidades reais; só os clubes são fictícios |
| `geography.districtArchetype` | Derivado do caráter factual da sede real | Sorteio produziria incoerência urbana |
| `crest.shieldShape` | Amostrado, **restrito por `countryId`** | Heráldica tem convenção regional; sortear global mistura escolas |
| `crest.centralCharge` | Autorado por âncora (`HeraldicSubstitution`, §6) | Exige verificação de símbolo real da cidade |
| `crest.motto` | Amostrado da lista de conteúdo, restrito por `countryId` | Decorativo |
| `crest.colors` | Derivado de `palette` | Não é fonte independente |
| `palette.*` | Derivado da âncora, **preservação cromática** (§7.1) | Vetor primário de reconhecimento |
| `palette.typographyStyle` | Amostrado | Decorativo, sem consumidor mecânico |
| `kits.home.fabricPattern` | Derivado da âncora | Junto com a cor, é vetor de reconhecimento |
| `kits.away.*` | Derivado do `home` por inversão de polo (§7.2) | Restrição funcional (colisão em transmissão) |
| `kits.collarStyle` / `fitStyle` | Amostrado | Decorativo |
| `stadium.name` | Autorado por âncora (toponímia/folclore local) | Exige verificação |
| `stadium.capacity` | Derivado da banda de prestígio (§10.1) | Alimenta receita de bilheteria |
| `stadium.atmosphereArchetype` | Derivado do caráter documentado da torcida | Alimenta modificador de mando |
| `stadium.pitchSurface` | Derivado da banda de prestígio | Riqueza do clube determina manutenção |
| `aiProfile.defaultTacticalStyle` | Derivado do estilo histórico documentado da âncora | Alimenta preset de `TeamTactics` |
| `aiProfile.derbyRivalClubId` | Derivado do grafo geográfico (§9.3) | Aresta entre entidades |

**Corolário:** "amostrado" significa sorteio no stream determinístico da §8. "Derivado" significa função pura da âncora ou da banda — **zero sorteio**, replayável sem consultar o PRNG. "Autorado" significa passe humano com verificação registrada no `deviationAudit`.

---

## 6. Regras de desvio (deviation patterns)

| Id | Mecânica | Exemplo do idioma PES |
| :--- | :--- | :--- |
| `Toponymic` | Substitui o nome pela **unidade geográfica maior** (condado, região, província). | Aston Villa → *West Midlands Village* |
| `RegionalCode` | Região + sufixo/letra desambiguadora, quando há dois clubes na mesma cidade. | Inter → *Lombardy A* |
| `EpithetLift` | Promove a **alcunha histórica** a nome oficial. | Stoke City → *Trad Bricks* |
| `Phonetic` | Vizinho fonético do nome real, dentro da janela da §6.2. | — |
| `HeraldicSubstitution` | Troca a carga central do escudo por **outro símbolo verificável da mesma cidade**. Nunca redesenho do símbolo original. | loba romana → silhueta de monumento local |

`HeraldicSubstitution` é ortogonal às quatro regras de nomenclatura: aplica-se **sempre**, ao escudo, independentemente da `NamingRule` escolhida.

### 6.1 Seletor de regra — **NOVO, travado**

A v1 travava "exatamente uma regra, sem cascata, erro em falha" mas **não dizia como a regra é escolhida**. Sem isso o gerador não é implementável. Decisão travada — tabela avaliada **de cima para baixo, primeira condição verdadeira vence**:

| Ordem | Condição sobre a `ReferenceAnchor` | Regra |
| :--- | :--- | :--- |
| 1 | A âncora tem alcunha histórica documentada e não-marcária | `EpithetLift` |
| 2 | Há ≥2 âncoras registradas na mesma `cityName` | `RegionalCode` |
| 3 | O nome real da âncora contém um topônimo | `Toponymic` |
| 4 | — (nenhuma acima) | `Phonetic` |

**Distinção que precisa ficar explícita** (alguém vai ler errado): isto é um **seletor determinístico**, não um fallback. O seletor roda uma vez, escolhe uma regra, e a partir daí a regra escolhida ou produz candidato válido ou **lança erro** — não existe "tenta a próxima". A proibição de fallback vale para recuperação de falha, não para uma tabela de decisão sobre propriedades da entrada.

A ordem não é arbitrária: `EpithetLift` primeiro porque produz os nomes mais memoráveis e o menor risco de IP (a alcunha costuma ser folclore popular, não marca registrada); `Phonetic` por último porque é o de maior risco de proximidade e o único que precisa de medição.

### 6.2 Camada fonética (`PhoneticDeviationService`)

Reaproveita o serviço já especificado para nomes de jogadores.

- **Codificação:** Double Metaphone sobre âncora e candidato.
- **Métrica:** Jaro-Winkler.
- **Janela de aceitação:** `0.55 ≤ similarity ≤ 0.80`.
  - `< 0.55` → irreconhecível; a paródia perde a função.
  - `> 0.80` → perto demais; risco de confusão.
- **Laço:** gera candidato → mede → aceita ou descarta. Teto de tentativas explícito; estourou, **lança erro** (não relaxa a janela).

A janela vem de pesquisa anterior e é o único limiar numérico travado neste doc.

---

## 7. Política cromática

### 7.1 Preservação — **TRAVADA**

**As cores não são desviadas.** Preservam a família cromática da âncora.

Justificativa: a cor é o vetor de reconhecimento que faz a paródia funcionar, e é o eixo de **menor** exposição jurídica — o risco de IP mora no nome, no escudo e no símbolo, não em "azul e preto". Desviar o matiz destruiria o reconhecimento sem reduzir exposição: pagaria o custo sem comprar o benefício. O desvio se concentra onde o risco está.

Isto contraria o pedido original de "alteração cromática" do template. É deliberado e agora **travado**, não mais em aberto.

**Nota de higiene:** os hex são aproximações autorais da *família* cromática, digitadas na passada de autoria. Não são amostrados de assets reais (escudo, camisa, material de marca) — amostrar cor de um asset real é copiar de um asset real.

### 7.2 Derivação do uniforme reserva — **NOVO, travado**

A preservação cromática trava o uniforme titular, o que deixava o reserva sem regra. O template pedia "alto contraste heráldico para evitar colisão cromática em transmissões".

**Regra travada — inversão de polo de luminância:**

1. Calcular a luminância relativa da `kits.home.shirt`.
2. Se a titular é escura, a reserva assume o **polo claro** da `palette`; se é clara, assume o **polo escuro**. O polo é sempre uma cor **já presente na `palette`** — o reserva não introduz matiz novo (isso reabriria o desvio cromático pela porta dos fundos).
3. `kits.away.fabricPattern` é amostrado dentre os padrões que **não** coincidem com o do titular.
4. Validar o par (titular, reserva) por diferença perceptual de cor em **CIELAB (ΔE)**. Abaixo do limiar → lança erro, e a âncora vai para autoria manual.

O limiar de ΔE **não está fixado aqui** — é calibração de *game feel* contra o renderizador real (mesma disciplina de `D-04`), não achado de pesquisa. Entra como dependência declarada na §11.

---

## 8. Determinismo

Todo sorteio consome um stream próprio:

```
seed = hash(masterSeed, "club_identity", clubId)
```

Isolado dos streams de partida, de geração de jogadores e do escudo. Garantia: `(ReferenceAnchor, seed) → ClubIdentity` é replayável bit a bit.

**Consequência da §5:** só os campos marcados como *amostrados* tocam o PRNG. Campos derivados são função pura da âncora ou da banda. Isso significa que **acrescentar um campo derivado no futuro não desloca a sequência do stream** e não invalida mundos já gerados — enquanto acrescentar um campo amostrado desloca. Regra: campos amostrados novos entram **no fim** da ordem de consumo do stream.

---

## 9. Decisões travadas nesta revisão

As sete em aberto da v1, mais as três divergências de template pendentes de confirmação. Todas resolvidas.

### 9.1 `displayCode` — **unicidade global**

Escopo: **global**, não por país.

Justificativa: competições continentais colocam clubes de países diferentes na mesma tabela e no mesmo placar. Unicidade por país exigiria uma regra especial no contexto continental — ou seja, um segundo caminho. Unicidade global é o caminho único.

Volume: 3 letras = 17.576 códigos contra ~640 clubes na base de ~32 ligas. Folga confortável.

**Derivação determinística:** lista ordenada de candidatos por clube — (1) três primeiras letras de `shortName`; (2) iniciais das palavras significativas de `officialName`; (3) duas primeiras de `shortName` + primeira de `cityName`; (4) primeira de `shortName` + duas primeiras de `cityName`. Diacríticos removidos, caixa alta. Toma o primeiro livre no registro global. Ordem de alocação = ordem de `clubId`, então o resultado independe da ordem de execução do lote. Lista esgotada → **lança erro**.

### 9.2 `foundingYear` — **década real preservada, ano amostrado dentro dela**

O ano de fundação é demografia factual, não expressiva — mesma categoria de `cityName`. Preservar a década mantém coerente a hierarquia narrativa de tradição (quem é "o clube velho" da cidade, quem é o arrivista) sem custo de IP. Amostrar o ano dentro da década evita reproduzir a data exata, que é o único ponto onde o número vira citação.

Âncora sem ano de fundação documentado → **lança erro**, vai para autoria manual. Sem invenção silenciosa.

### 9.3 Rivalidade — **aresta dirigida, assimetria legal**

`derbyRivalClubId` responde "quem é o clássico **deste** clube". É propriedade deste clube.

Exigir simetria obrigatória significaria que gerar a identidade do clube A **escreve** no registro do clube B — o gerador passaria a ter efeito colateral fora da entidade que está gerando, violando *separation of concerns* e criando duas fontes de verdade para a mesma aresta.

Assimetria é, além disso, o caso interessante: o clube pequeno que se define pelo ódio ao grande, e o grande que mal o registra. Sem validação de reciprocidade. `null` é legal (clube sem clássico).

Um grafo de rivalidades completo (intensidade, múltiplos rivais, histórico) é **outro sistema**, não este. Aqui há exatamente um campo, e ele aponta para um id.

### 9.4 `DistrictArchetype` → efeito mecânico — **v1 = zero efeito, campo emitido**

O simulador de vida/carreira que consumiria esse bônus **não está desenhado**. Especificar agora qual variável ele toca e com que magnitude significaria escolher, hoje, uma interface para um módulo inexistente — e quase certamente escolher errado, criando débito de calibração e conflito de verdade quando o módulo chegar.

Decisão: o campo é **emitido e persistido**, com **nenhum consumidor em v1**. Isso evita migração de schema depois e não custa nada agora. O bônus concreto é especificado no doc do simulador de vida, quando ele existir, e este doc não o antecipa.

Isto é *deferred abstraction* aplicado corretamente: generalizar no segundo caso de uso real, não no primeiro imaginado.

### 9.5 `AtmosphereArchetype` → efeito mecânico — **mecanismo travado, magnitude é dependência declarada**

`D-06` já trava que `AtmosphereArchetype` **nunca toca a física**. O que sobra é exatamente um consumidor: o modificador de mando.

Mecanismo travado: o arquétipo mapeia para um único parâmetro nomeado `homeAdvantageModifier`, ordinal, consumido pelo resolvedor de partida. Um parâmetro, um consumidor, um caminho.

A magnitude **não é inventada aqui**. Vantagem de casa por liga é grandeza empírica publicada, e o harness de calibração de tier já precisa ancorá-la de qualquer forma (a monotonicidade ordinal exige "vantagem de casa positiva" como critério). Não é uma pesquisa nova — é o mesmo dado, consumido por dois sistemas. Vira dependência declarada na §11, não questão em aberto.

### 9.6 `capacity` → faixas por banda — **derivação travada, tabela é dependência declarada**

Derivação travada (já implícita na v1, agora explícita): o gerador **recebe** a banda de prestígio do clube e amostra por **Gaussiana truncada** dentro da faixa da banda. Nunca escrito à mão. Mesma disciplina da geração CA/PA.

As bandas são as **B1–B6** do doc de Prestígio de Competição — não uma escala nova. A tabela `banda → (média, desvio, mín, máx)` precisa de dados reais de capacidade de estádio, que são públicos e abundantes. É tarefa de pesquisa **limitada e nomeada**, não questão de design. §11.

### 9.7 Chaves em inglês e rival por id

Travadas em §2 e §3 respectivamente. Eram as duas divergências de template que a v1 deixou "para revisar"; estão fechadas.

---

## 10. Consumidores a jusante

| Sistema | Lê | Status em v1 |
| :--- | :--- | :--- |
| `CrestGenerator` | `crest.shieldShape`, `crest.colors`, `crest.centralCharge` | Ativo |
| `KitAssembler` | `kits.*`, `palette.typographyStyle` | Ativo |
| `Stadium` (entidade) | `capacity` → bilheteria; `pitchSurface` → física de rolagem/quique (Tier 1) | Ativo |
| `Stadium` → resolvedor | `atmosphereArchetype` → `homeAdvantageModifier` | Mecanismo definido, magnitude pendente (§11) |
| `TeamTactics` | `aiProfile.defaultTacticalStyle` → preset de parâmetros | Ativo — é *preset*, não caminho de código separado |
| Calendário / narrativa | `aiProfile.derbyRivalClubId` → aresta de rivalidade | Ativo |
| Vida/carreira | `geography.districtArchetype` | **Sem consumidor em v1** (§9.4) |

### 10.1 Dependência de banda

`capacity` e `pitchSurface` são os dois campos derivados da banda de prestígio. Ambos precisam que a banda do clube esteja atribuída **antes** da geração de identidade. Ordem no pipeline de autoria: registro de mundo (atribui `clubId` + banda) → `ClubIdentityGenerator` → `CrestGenerator` / `KitAssembler`.

---

## 11. Dependências de dados (não são decisões em aberto)

Três números não entram neste doc porque inventá-los criaria débito de calibração. Cada um vira tarefa limitada com entregável nomeado — não "discutir depois".

| # | Número | Origem exigida | Entregável |
| :--- | :--- | :--- | :--- |
| 1 | `homeAdvantageModifier` por `AtmosphereArchetype` | Vantagem de casa por liga (dado público); **já exigido pelo harness de monotonicidade ordinal** | Tabela ordinal de 5 linhas |
| 2 | `capacity` por banda B1–B6 | Capacidades reais de estádio por tier de liga (dado público) | Tabela de 6 linhas: média, desvio, mín, máx |
| 3 | Limiar de ΔE (CIELAB) para colisão titular × reserva | **Não é pesquisa** — é calibração de *game feel* contra o renderizador (`D-04`) | Um float, fixado no primeiro teste visual |

O #3 não pode ser resolvido antes de existir renderizador. Os #1 e #2 podem ser resolvidos hoje, e o #1 não é trabalho novo.

---

## 12. Build path incremental

1. **Schema + enums + validador.** Escrever um `ClubIdentity` à mão (usar o §13), carregar no jogo, ver o clube existir. Prova o contrato.
2. **Seletor de regra (§6.1) + `Toponymic` e `EpithetLift`** sobre 3 âncoras manuais. Duas regras confirmam o padrão.
3. **`PhoneticDeviationService`** plugado, janela ativa, erro no estouro.
4. **Registro de `displayCode`** (§9.1) — a primeira colisão real prova a regra de desempate.
5. **Ligação ao `CrestGenerator`** — o primeiro escudo saindo do dado.
6. **Lote.** Rodar sobre as âncoras de uma liga inteira; revisar o `deviationAudit` linha a linha, gate em `anchorFactsVerified`.

Abstração só depois de duplicação real (a regra 3 confirma o padrão, não a regra 1).

---

## 13. Instância de referência — prova de contrato

Uma instância trabalhada, para o passo 1 da §12. Âncora escolhida no **Brasileirão Série A**, a liga-piloto do projeto.

**⚠️ Todo fato da âncora abaixo precisa passar pelo gate `anchorFactsVerified` antes de virar registro de lote.** Foram escritos de memória e não verificados em fonte nesta sessão. Serve como prova de forma do contrato, não como dado de produção.

**`ReferenceAnchor`** (repositório de autoria — não empacotado): clube = Grêmio Foot-Ball Porto-Alegrense; cidade = Porto Alegre.

**Seletor (§6.1):** condição 1 verdadeira — alcunha histórica documentada ("Imortal"), não-marcária. → `EpithetLift`. Não avalia as demais.

```json
{
  "clubId": "clb_bra_poa_001",
  "identity": {
    "officialName": "Imortal Guaibense",
    "shortName": "Imortal",
    "displayCode": "IMO",
    "nickname": "Os Imortais",
    "foundingYear": 1906
  },
  "geography": {
    "cityName": "Porto Alegre",
    "countryId": "BRA",
    "districtArchetype": "WorkingClass"
  },
  "crest": {
    "shieldShape": "Heater",
    "centralCharge": "Silhueta da chaminé da usina termelétrica à beira do Guaíba",
    "colors": ["#1C4E9C", "#000000", "#FFFFFF"],
    "motto": "Semper Immortalis"
  },
  "palette": {
    "primary": "#1C4E9C",
    "secondary": "#000000",
    "tertiary": "#FFFFFF",
    "typographyStyle": "ClassicSerif"
  },
  "kits": {
    "collarStyle": "Grandad",
    "fitStyle": "Regular",
    "home": { "fabricPattern": "VerticalStripes", "shirt": "#1C4E9C", "shorts": "#000000", "socks": "#FFFFFF" },
    "away": { "fabricPattern": "Solid", "shirt": "#FFFFFF", "shorts": "#FFFFFF", "socks": "#1C4E9C" }
  },
  "stadium": {
    "name": "Arena Farroupilha",
    "capacity": 52000,
    "atmosphereArchetype": "Cauldron",
    "pitchSurface": "Pristine"
  },
  "aiProfile": {
    "defaultTacticalStyle": "Controlled",
    "derbyRivalClubId": null
  }
}
```

**Leitura do registro, campo a campo:**

- `officialName` — `EpithetLift` promove "Imortal"; "Guaibense" é o gentílico do Guaíba, geografia real da cidade. Zero fragmento do nome real.
- `foundingYear` — década real (1900) preservada, ano amostrado dentro dela (§9.2). Não coincide com o ano real: é o comportamento correto da regra.
- `districtArchetype` — derivado do caráter industrial da zona da sede real.
- `crest.centralCharge` — `HeraldicSubstitution`: monumento verificável de Porto Alegre substituindo a carga original. **`sourceCitation` pendente** no `deviationAudit`.
- `palette` — preservação cromática (§7.1): azul, preto e branco, a família da âncora. Hex autorais.
- `kits.home.fabricPattern` — `VerticalStripes`, derivado (é vetor de reconhecimento, §5).
- `kits.away` — inversão de polo (§7.2): titular escura → reserva assume o polo claro **da própria paleta**. Nenhum matiz novo. Padrão amostrado fora do titular.
- `stadium.name` — folclore regional (Revolução Farroupilha), não nome de clube.
- `capacity` — **placeholder**. Só vira valor legítimo quando a tabela da §11 #2 existir; hoje está escrito à mão, que é exatamente o que a §5 proíbe.
- `derbyRivalClubId` — `null`. A âncora tem clássico óbvio, mas o clube-alvo ainda não foi gerado e a aresta aponta para um **id**, não para um nome (§9.3). Preencher quando `clb_bra_poa_002` existir.

O registro exercita `EpithetLift`, `HeraldicSubstitution`, preservação cromática, inversão de polo, década preservada e aresta nula — seis regras num só caso. Serve de teste de fumaça do validador.

---

## 14. O que isto destrava

Com o seletor de regra, a política de proveniência e a derivação do reserva fechados, o módulo é **implementável de ponta a ponta** — o que a v1 não era, mesmo com o schema completo.

Pendências reais reduzidas de sete decisões em aberto para **três números com origem nomeada** (§11), dos quais um já é trabalho exigido por outro sistema e outro não pode ser resolvido antes do renderizador existir.

`CrestGenerator` e `KitAssembler` ganham a entrada de que precisavam. A base de mundo (~32 ligas) passa a ser trabalho de conteúdo, não de design — com a ressalva de que a banda de prestígio precisa estar atribuída antes (§10.1).
