# Terra Paralela — Geração de Identidade de Clube (Club Identity)

> Companion doc do GDD. Cross-ref: **Club & World Data** (modelo de dados enxuto: identidade / finanças / estádio / instalações / diretoria) · **Gerador de Escudos** (pipeline por camadas) · **Tactics → Behavior** (`TeamTactics`) · **Prestígio de Competição** · GDD §5 (LOD).
> Convenção: prosa PT-BR, **identificadores em inglês**. Determinismo obrigatório (SplitMix64, stream isolado).
>
> **Escopo:** como uma âncora real vira um clube fictício completo, coerente e deslicenciado — nome, alcunha, geografia, escudo, cores, uniformes, estádio e comportamento de IA. É a camada de *autoria de mundo*, não de simulação.

---

## 1. Papel do sistema e fronteira

O `ClubIdentityGenerator` é uma ferramenta de **autoria de conteúdo (pipeline offline)**, não um sistema de runtime. Ele roda no editor/ferramenta de mundo, produz um `ClubIdentity` serializado, e o jogo **apenas lê** esse dado.

Consequência direta (princípio *separation of concerns*): nenhuma regra de paródia, nenhuma métrica fonética e nenhuma tabela de desvio existe no build do jogo. O que chega ao jogador é dado inerte.

| | |
| :--- | :--- |
| **Entrada** | `ReferenceAnchor` (clube real + cidade real) — usado **apenas** como semente conceitual, nunca persistido no build. |
| **Saída** | `ClubIdentity` (JSON/`Resource`), consumido por `CrestGenerator`, `KitAssembler`, entidade `Stadium`, `TeamTactics` default. |
| **LOD** | Irrelevante. Roda zero vezes em runtime, em qualquer tier. |

**Regra de higiene de IP (não-negociável).** A `ReferenceAnchor` e o campo de auditoria de desvio ficam no repositório de autoria. Não são empacotados. O build final não contém nenhum nome real de clube.

---

## 2. Correção de convenção — chaves em inglês

O template original da feature veio com chaves JSON em PT-BR (`metadados_identificacao`, `cor_primaria_hex`…). Isso viola a convenção do projeto (*prosa PT-BR, identificadores em inglês*) e criaria um caminho de nomenclatura paralelo à base de código C#. **Corrigido aqui:** chaves em inglês, valores e prosa em PT-BR.

Mapa de tradução (para referência histórica; o template PT-BR fica descontinuado — *one way*):

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
    "derbyRivalClubId": "string"
  }
}
```

**Notas de contrato:**

- `derbyRivalClubId` é **referência por id**, nunca nome literal. O template original pedia "nome fictício recomendado do rival" — isso duplicaria a fonte de verdade. A rivalidade é uma aresta entre entidades já existentes na árvore de contenção geográfica.
- `crest.colors` é subconjunto derivado de `palette`, não uma paleta independente. Restrição: 2–3 cores, para bordado/merchandise e para o compositor de camadas do `CrestGenerator`.
- `deviationAudit` é campo de **autoria**, não de dado: registra qual regra da §5 produziu o nome. Serve à revisão de IP e ao debug do gerador. Vive no repositório, fora do build.

---

## 4. Vocabulários canônicos (enums)

Confirmados em sessão. Identificador em inglês, rótulo de UI em PT-BR, efeito mecânico explícito. **Todo enum é fechado; valor fora da faixa lança erro (fail-fast).**

### `DistrictArchetype` — bônus urbano no simulador de carreira
| Id | Rótulo |
| :--- | :--- |
| `WorkingClass` | Operário/Industrial |
| `Docklands` | Portuário |
| `HistoricCenter` | Centro Histórico |
| `Affluent` | Residencial Nobre |
| `University` | Universitário |
| `Outskirts` | Periférico |
| `Coastal` | Litorâneo |

### `ShieldShape` — camada 1 do `CrestGenerator`
`Heater` (Clássico) · `Round` (Redondo) · `Oval` · `Square` (Quadrado) · `Ogival`

### `AtmosphereArchetype`
`Cauldron` (Caldeirão) · `Traditional` (Tradicional/Familiar) · `Corporate` (Corporativo/Moderno) · `Hostile` (Hostil) · `Apathetic` (Apático/Frio)

### `PitchSurface` — entra na física de rolagem/quique
`Pristine` (Impecável, rápido) · `Heavy` (Pesado/Irregular, lento) · `Synthetic` (Sintético) · `Worn` (Castigado)

### `TacticalStyle` — preset de `TeamTactics` da IA
`Possession` (Posse de Bola) · `Counter` (Contra-ataque) · `HighPress` (Pressão Alta) · `LowBlock` (Retranca) · `Controlled` (Cadenciado) · `Direct` (Futebol Direto)

### `TypographyStyle`
`ModernSans` · `ClassicSerif` · `Blackletter` (Gótica) · `Geometric` · `Stencil`

### `CollarStyle`
`VNeck` (V) · `Polo` · `Crew` (Careca) · `Grandad` (Padre) · `ButtonedSport` (Esporte c/ botões)

### `FitStyle`
`Slim` · `Regular` · `Retro` (Retrô/Largo)

### `FabricPattern`
`Solid` (Liso) · `VerticalStripes` · `HorizontalStripes` · `Sash` (faixa diagonal) · `ContrastSleeves` · `Pinstripes` · `Checks` (Xadrez)

> **Fronteira arquitetura × conteúdo.** Os enums são *arquitetura* — cada valor tem consumidor mecânico (física, IA, compositor de escudo, montador de kit) e adicionar um valor exige código. A lista de `centralCharge` e de `motto` é *conteúdo*: cresce livremente, sem tocar em código.

---

## 5. Regras de desvio (deviation patterns)

Cada regra tem gatilho, mecânica e o motivo de existir. O `deviationAudit` registra qual foi aplicada.

| Id | Mecânica | Exemplo do idioma PES |
| :--- | :--- | :--- |
| `Toponymic` | Substitui o nome do clube pela **unidade geográfica maior** (condado, região, província). | Aston Villa → *West Midlands Village* |
| `RegionalCode` | Região + sufixo/letra desambiguadora, quando há dois clubes na mesma cidade. | Inter → *Lombardy A* |
| `EpithetLift` | Promove a **alcunha histórica** a nome oficial. | Stoke City → *Trad Bricks* |
| `Phonetic` | Vizinho fonético do nome real, dentro da janela de similaridade da §6. | — |
| `HeraldicSubstitution` | Troca a carga central do escudo por **outro símbolo verificável da mesma cidade** (monumento, fauna, ofício). Nunca redesenho do símbolo original. | loba romana → silhueta de águia/estátua local |

**Regra de Preservação Cromática (v1).** As cores **não** são desviadas: preservam a família cromática da âncora. Justificativa: a cor é o vetor de reconhecimento que faz a paródia funcionar, e é o eixo de menor exposição — o risco de IP mora no nome, no escudo e no símbolo, não em "vermelho e branco". O desvio se concentra onde o risco está.

Isto contraria o pedido original de "alteração cromática". É deliberado, e está registrado como decisão em aberto (§9) por ser decisão de design, não de pesquisa.

**Seleção de regra.** Uma âncora recebe **exatamente uma** regra de nomenclatura (`Toponymic` | `RegionalCode` | `EpithetLift` | `Phonetic`). Sem cascata, sem fallback: se a regra escolhida não produzir candidato válido, o gerador **lança erro** e a âncora vai para revisão manual. Não existe "tenta a próxima".

---

## 6. Camada fonética (`PhoneticDeviationService`)

Reaproveita o serviço já especificado para geração de nomes de jogadores.

- **Codificação:** Double Metaphone sobre âncora e candidato.
- **Métrica:** Jaro-Winkler.
- **Janela de aceitação:** `0.55 ≤ similarity ≤ 0.80`.
  - `< 0.55` → irreconhecível; a paródia perde a função.
  - `> 0.80` → perto demais; risco de confusão.
- **Laço:** gera candidato → mede → aceita ou descarta. Teto de tentativas explícito; estourou, **lança erro** (não relaxa a janela).

A janela vem de pesquisa anterior. Nenhum outro limiar numérico deste doc está fixado sem origem — ver §9.

---

## 7. Determinismo

Todo sorteio do gerador consome um stream próprio:

```
seed = hash(masterSeed, "club_identity", clubId)
```

Isolado dos streams de partida, de geração de jogadores e do escudo. Garantia: `(ReferenceAnchor, seed) → ClubIdentity` é replayável bit a bit. Regerar a base de mundo com a mesma seed produz o mesmo mundo.

---

## 8. Consumidores a jusante

| Sistema | Lê |
| :--- | :--- |
| `CrestGenerator` | `crest.shieldShape` → camada *shape*; `crest.colors` → *palette*; `crest.centralCharge` → *charges*. O gerador **não** conhece a âncora. |
| `KitAssembler` | `kits.*` + `palette.typographyStyle`. |
| `Stadium` (entidade) | `stadium.capacity` → receita de bilheteria; `atmosphereArchetype` → modificador de mando; `pitchSurface` → física de rolagem/quique no Tier 1. |
| `TeamTactics` | `aiProfile.defaultTacticalStyle` → preset de parâmetros (mentalidade, largura, linha, pressão, ritmo, diretividade). É *preset*, não caminho de código separado. |
| Calendário / narrativa | `aiProfile.derbyRivalClubId` → aresta de rivalidade. |
| Vida/carreira | `geography.districtArchetype` → contexto urbano. |

`stadium.capacity` **não é escrito à mão**. É derivado da faixa do clube no modelo de dados de clube (mesma disciplina da geração CA/PA por Gaussiana truncada). O gerador de identidade recebe a faixa e sorteia dentro dela.

---

## 9. Decisões em aberto

1. **Política cromática.** Preservação (v1, §5) vs. desvio de matiz. Se desvio: qual delta, e com que base? Não há dado — é decisão de design, e nenhum número entra antes dela.
2. **Faixas de `capacity` por tier de clube** — dependem do modelo de dados de clube estar fechado.
3. **Efeito mecânico de `AtmosphereArchetype`** — modificador de mando existe, magnitude não. Precisa de ancoragem empírica (vantagem de casa por liga) antes de virar número.
4. **`DistrictArchetype` → bônus concreto** de carreira/vida. Qual variável ele toca? Ainda não especificado.
5. **Unicidade de `displayCode`** (3 letras) — escopo global ou por país? Colisão em 32 ligas é certa. Precisa de regra de desempate determinística.
6. **`foundingYear`** — livre, ou coerente com a história fictícia da liga?
7. **Rivalidade** — simétrica obrigatória (A↔B) ou permite assimetria (o clube pequeno que odeia o grande)?

---

## 10. Build path incremental

1. **Schema + enums + validador.** Escrever um `ClubIdentity` à mão, carregar no jogo, ver o clube existir. Prova o contrato.
2. **Regras de nomenclatura** (`Toponymic`, `EpithetLift`) sobre 3 âncoras manuais. Duas regras confirmam o padrão.
3. **`PhoneticDeviationService`** plugado, janela ativa, erro no estouro.
4. **Ligação ao `CrestGenerator`** — o primeiro escudo saindo do dado.
5. **Lote.** Rodar sobre a lista de âncoras de uma liga inteira; revisar o `deviationAudit` linha a linha.

Abstração só depois de duplicação real (regra 3 confirma o padrão, não a regra 1).

---

## 11. O que isto destrava

Com o schema fechado, o `CrestGenerator` e o `KitAssembler` ganham a entrada de que precisavam, e a base de mundo (~32 ligas) passa a ser trabalho de conteúdo, não de design. **A primeira instância ainda não pode ser gerada:** o template chegou sem `ReferenceAnchor` — clube e cidade em branco.
