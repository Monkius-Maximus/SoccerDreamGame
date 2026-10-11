# Terra Paralela — Registro de Decisões: Calibração e Autoria de Mundo (D-38…D-52)

> **Natureza.** Registro de decisões que fecha as três dependências de dados declaradas na §11 do *Geração de Identidade de Clube v2*, mais cinco itens de arquitetura levantados pelo primeiro lote real de autoria. Escrito neste formato para respeitar **D-08** (moratória de companion docs novos), no mesmo padrão dos registros de *Geografia e Competições* e *Camada de Autoria de Mundo*.
>
> **Cross-ref:** *Geração de Identidade de Clube v2* (§5, §6.1, §6.2, §9.1, §9.5, §9.6, §11) · *Criação de Personagem e Ciclo de Vida* (§2.1, §2.2, §5.1, §5.4, §9.4) · *Sistema de Prestígio de Competições* (bandas B1–B6) · *Registro de Decisões — Regras e Competições* (RC-03) · `SimCore.cs` (`PositionWeights.Overall`) · `tp_import.py` (contrato de terceirização) · *Lote de Autoria — Liga Brasileira*.
>
> **Numeração.** Continua D-01…D-37. **Ressalva de segurança, como nos registros anteriores:** esta sessão não tem visibilidade sobre decisões tomadas em outras conversas. Se D-38+ já estiver ocupado, renumerar na reconciliação.
>
> **Fora de escopo:** match engine, steering, física, Tier 1.5. Nada aqui reabre D-01…D-09.

---

## 0. O que mudou de estado

| Item | Antes | Agora |
| :--- | :--- | :--- |
| §11 #1 `homeAdvantageModifier` | Mecanismo definido, magnitude ausente | **RESOLVIDO** — tabela ordinal ancorada (D-42) |
| §11 #2 `capacity` por banda | Tabela provisória inventada | **RESOLVIDO, com achado que muda o desenho** (D-39) |
| §11 #3 Limiar de ΔE | Não resolvível antes do renderizador | **Continua bloqueado.** Sem mudança. |
| §3.1 `crest.sourceCitation` | PENDENTE nas 20 linhas | **RESOLVIDO** — URL verificável nos 20 |
| `anchorFactsVerified` | FALSO em 100% | **VERDADEIRO em 19 de 20** |
| Banda de prestígio por clube | Improvisada no lote | **TRAVADA** (D-38) |
| Arredondamento de OVR | Divergente entre planilha e C# | **TRAVADO e APLICADO no `SimCore.cs` real** (D-40) |
| Débito de calibração de OVR | Teto 79 contra alvo 86–88 | **RESOLVIDO** — teto 88, dentro do alvo (D-46 + D-49) |
| Bug do arquétipo de goleiro | Não detectado — 73 GKs com 35% do OVR em ruído | **CORRIGIDO** (D-49) |
| Vetor de OVR do goleiro | 4 atributos, Σw² 0,275, cauda deformada | **REESCRITO** para 7 atributos (D-49) |
| "Tratamento de goleiro dentro dos 12" | Aberto no ledger desde *Criação de Personagem* §2.1 | **FECHADO** (D-49) |
| Capacidade B5 | Interpolado, sem fonte | **RESOLVIDO** — Thai League 1 (D-46 §11.1) |
| `Mirassol.anchorFactsVerified` | FALSO (sem fonte de distrito) | **RESOLVIDO** — laudo FPF (D-46 §11.1) |
| Hipótese D-45 (nível 0 inverte na Europa) | Registrada, não testada | **TESTADA e CONFIRMADA** — Eredivisie (D-47) |
| Partição Physical/Technical/Mental (10 restantes) | Aberta, mas já estrutural em código (D-48) | **TRAVADA** (D-52) — igual ao `ATTR_GROUP` já em produção |
| Curva de declínio do grupo `Goalkeeping` | Sem regra — nem `PhysDecay` nem `TechDecay` | **DECIDIDA** — entra em `TechDecay` (D-52) |
| `CapProfile` — fórmula por posição | Assinatura travada (T-04), tabelas inexistentes | **FÓRMULA TRAVADA** (D-50). Código e aterrissagem no lote continuam pendentes, por design (atrás de D-07) |
| Regra de apelido, 4 pernas (limiar de 50 anos + fonte de `band`/`clubStrength`) | Implementada em `tp_import.py`, citada como "D-48" num comentário, nunca travada no ledger | **TRAVADA** (D-51). Débito descoberto: o lote de 20 clubes reprova em `bandSource`/`strengthSource` — backfill fica para tarefa nomeada, não fechado aqui |

> **Nota de sessão (2026-08-17, rodada 4).** A §14 fecha as três pontas que a rodada 3 deixou em aberto: a fórmula de `CapProfile` (D-50), a regra de apelido de 4 pernas que já estava implementada mas não travada (D-51), e a partição final dos 12 atributos com a curva de declínio do goleiro (D-52). Nenhuma pesquisa nova em D-51/D-52 — as duas formalizam o que já estava em código. D-50 é formulação nova, mas deliberadamente não executada contra o lote nesta rodada: `CapProfile` é mecânica de criação de personagem, que não existe em código ainda (atrás de D-07), então travar a fórmula sem regenerar o lote é a decisão certa, não uma tarefa deixada pela metade.
>
> **Nota de sessão (2026-08-15, rodada 3).** A §13 fecha o débito de OVR de vez. Ele não caiu por mais calibração: caiu porque puxar a ponta solta do ledger (partição dos 12 / `CapProfile`) expôs um **bug** que mantinha os 73 goleiros do lote com um terço do peso do OVR preso em ruído. Corrigido o bug, o teto foi de 85 para 88 — dentro do alvo — e o vetor de OVR do goleiro precisou ser reescrito.
>
> **Nota de sessão (2026-08-14, rodada 2).** As quatro linhas acima e as §11–§12 abaixo fecham os itens que a rodada 1 deste registro deixou deliberadamente em aberto (§9 original). Escopo desta rodada: dois itens de limpeza pontual (B5, Mirassol), o débito de OVR (pesquisa dirigida + recalibração), o patch D-40 aplicado ao código real, e o teste da hipótese D-45 contra uma segunda liga real.

---

## 1. D-38 — `Club.prestigeBand` é campo **autorado**, não derivado

**Problema.** `ClubIdentity` §10.1 exige a banda do clube atribuída *antes* da geração (é dela que saíam `capacity` e `pitchSurface`). Mas as bandas B1–B6 do doc de Prestígio são definidas para **competições**. Não havia regra para banda de clube, e o lote improvisou.

**D-38 — `Club.prestigeBand` é campo autorado do registro de mundo. `Competition.prestigeBand` continua derivado dos participantes (RC-03, intocado). A direção é clube → competição, sempre.**

Justificativa: derivar a banda do clube da banda da competição, e a da competição dos clubes, é circular. Uma das duas tem de ser insumo. A escolha é forçada pelo RC-03, que já travou a competição como derivada — inverter agora reabriria uma decisão fechada por um problema que não é dela.

Consequência que o lote não previa: **um clube pode ter banda melhor que a da liga em que joga.** Isso não é anomalia, é o caso normal do clube grande em liga fraca (e é exatamente o que o modelo de *fit* de desenvolvimento do §9.3 da *Criação de Personagem* precisa para funcionar — um jovem em elenco de elite jogando liga fraca).

Não é violação de "estado derivado nunca é verdade": prestígio de clube não deriva de nada mais primitivo. É fato de mundo autorado, como `cityName`.

**Nota (D-51, 2026-08-17):** "autorado" não é sinônimo de "sem fonte". Ver §14.2 — a banda ainda precisa citar a posição competitiva real da âncora.

---

## 2. D-39 — `capacity` sai do **perfil de estádio do país**, não da banda

### 2.1 O achado

A tabela `banda → (média, desvio)` da §11 #2 foi construída sobre **270 estádios reais** de 14 ligas. O resultado refuta a premissa da §5:

| Banda | Média | Desvio | n | Amostra |
| :--- | ---: | ---: | ---: | :--- |
| B1 | 38.833 | 18.807 | 95 | Bundesliga, PL, Serie A, La Liga, Ligue 1 |
| B2 | 24.020 | 10.973 | 42 | Championship, Eredivisie |
| B3 | **26.875** | 16.963 | 83 | Süper Lig, Serie B ITA, MLS, Allsvenskan |
| B4 | 25.604 | 13.988 | 30 | K League 1, Ekstraklasa |
| B5 | 21.871 | 10.394 | **0** | **INTERPOLADO — sem fonte** |
| B6 | 18.138 | 6.799 | 20 | 3. Liga alemã (3ª divisão; a 4ª não foi encontrada) |

**A capacidade não é monotônica na banda.** B3 (26.875) é maior que B2 (24.020), e B4 (25.604) empata com B3. Só B1 e B6 se separam com clareza. E o Brasileirão, que é B2, tem média 43.931 — **maior que a B1 europeia**, com desvio (19.159) maior que o da Premier League.

Isto não é ruído amostral: capacidade de estádio mede herança urbana e população, não força esportiva. A Eredivisie é B2 com média 20.468; o Brasileirão é B2 com 43.931. A banda não explica.

**Verificação por consequência.** Rodei o lote com a derivação literal por banda antes de decidir. A saída foi um clube B3 com estádio de 93.000 lugares e um B2 com 20.500 na mesma liga. Absurdo visível, não hipótese.

### 2.2 A decisão

**D-39 — A capacidade é amostrada por Gaussiana truncada sobre o `countryStadiumProfile` (média, desvio, mín, máx por país e divisão, de dado real). A banda do clube apenas **desloca** o clube dentro dessa distribuição, em `0,45` desvio por degrau abaixo da banda da liga. Monotônico por construção.**

```
COUNTRY_STADIUM_PROFILE["BRA"] = (44000, 19000, 12000, 79000)   # 20 estádios reais da Série A 2026
shift = (rank(bandaDaLiga) − rank(bandaDoClube)) × 0,45
capacity = TruncNormal(μ, σ, mín, máx; deslocada por shift)
```

País sem perfil → **lança**, vai para autoria manual. Sem invenção silenciosa.

**A tabela por banda continua existindo** — é o entregável do §11 #2 e serve para *montar* o perfil de países sem dado direto. É insumo de autoria, **não** um segundo caminho de runtime. Isso preserva *one way*.

**Emenda declarada à §5:** a linha `stadium.capacity | Derivado da banda de prestígio` passa a ler `Derivado do perfil de estádio do país, deslocado pela banda`. `stadium.pitchSurface` **não** muda — riqueza do clube realmente determina manutenção de gramado, e ali a banda é o preditor certo.

**Resultado no lote:** média 37.900 contra 43.931 real, desvio 14.289 contra 19.159, faixa 12.500–62.000 contra 12.000–78.838. O deslocamento por banda comprime a cauda alta, porque a maioria dos clubes está na banda da liga ou abaixo. Aceitável; se incomodar, o parâmetro está exposto.

---

## 3. D-40 — Convenção de arredondamento: **9 casas, depois half-up**

Duas divergências, não uma:

1. `MathF.Round` do C# usa **banker's rounding** (`ToEven`); `ROUND` do Excel/LibreOffice usa **meio para cima**. Empates exatos de `.5` divergem.
2. A soma ponderada em ordem diferente produz `51,49999999999999` onde a planilha produz `51,5` exato. **Corrigir só o modo de arredondamento não basta** — o empate deixa de ser empate antes de o modo importar.

**D-40 — A convenção única do projeto é: arredondar a soma ponderada a 9 casas decimais, depois arredondar para inteiro meio-para-cima.**

```csharp
public static int Overall(Position pos, IReadOnlyDictionary<Attr, int> attrs)
{
    if (!Weights.TryGetValue(pos, out var w))
        throw new ArgumentException($"Posição {pos} sem pesos definidos");

    double total = 0d;
    foreach (var (attr, weight) in w)
        total += attrs[attr] * (double)weight;   // KeyNotFound = bug real, deixe estourar

    // D-40: a soma em ordem diferente produz 51,49999999999999 onde a planilha de
    // autoria produz 51,5 exato. Arredondar a 9 casas ANTES restaura o empate;
    // AwayFromZero então resolve o empate como o Excel resolve. Sem as duas coisas,
    // engine e planilha discordam em ~0,4% das linhas.
    total = Math.Round(total, 9, MidpointRounding.AwayFromZero);
    return (int)Math.Round(total, MidpointRounding.AwayFromZero);
}
```

Note a troca de `float` para `double` no acumulador: a soma de 12 termos em `float` tem erro suficiente para deslocar o empate mesmo com o arredondamento a 9 casas.

Sem isso, qualquer harness que compare saída de planilha com saída de engine acusa 3 a 5 divergências em 688 e o tempo vai embora procurando bug de lógica onde há artefato de ponto flutuante.

**Estado (rodada 2026-08-14): APLICADO.** O patch acima foi escrito diretamente no `SimCore.cs` do projeto — deixou de ser trecho proposto no registro para ser o código real. Ver §11.

---

## 4. D-41 — Gate de pronunciabilidade no `PhoneticDeviationService`

A janela `0,55 ≤ Jaro-Winkler(Double Metaphone) ≤ 0,80` é **necessária e insuficiente**. Ela mede distância fonética, não legibilidade. Rodando 288 desvios de sobrenome, a janela sozinha aceitou `Azasmbraeta`, `Penhrurdo`, `Nifuezendes` e `Germorr` — todos dentro da faixa, todos impronunciáveis.

**D-41 — O `PhoneticDeviationService` aplica um gate de pronunciabilidade sobre o candidato, além da janela numérica. Candidato reprovado no gate é descartado como se estivesse fora da janela.**

Regras do gate (dependentes de idioma, parametrizadas por `countryId`):
1. Nenhum aglomerado consonantal de 4+ letras.
2. Aglomerados de 2 e 3 letras só dentro de uma whitelist do idioma.
3. Coda final simples: a última letra é vogal ou uma de `s z r l m n`, e a penúltima é vogal.

**Padrão empírico dos que estouram o teto** (18 dos 288, ou 6,3%), útil para dimensionar a fila de autoria manual em lotes futuros:
- **Sobrenomes curtos** (Alves, Rossi, Vaz): o código Metaphone tem 2–3 caracteres e o bônus de prefixo do Jaro-Winkler domina — o candidato ou fica em 1,0 ou desaba abaixo de 0,55. Não há vizinhança na janela sem alongar o radical.
- **Sobrenomes de origem não-portuguesa** (Schmidt, Schwengber, Battaglia, Vojnovic, Kannemann): o Metaphone colapsa os aglomerados de forma não-intuitiva.

Consequência de dimensionamento: **espere ~6% de autoria manual por lote de nomes**, concentrada nesses dois grupos. Não é falha do serviço; é o teto de tentativas funcionando como especificado.

---

## 5. D-42 — `homeAdvantageModifier`: ordinal, **componente torcida apenas**

### 5.1 O que o dado diz

A vantagem de casa total varia de 48,2% a 86,8% dos pontos entre 157 ligas nacionais (Pollard & Gómez 2014, 169.752 jogos). Mas **a maior parte dessa variação não é torcida** — é viagem (+0,115 gol por 1.000 km percorridos pelo visitante, no Brasil), altitude, e fatores institucionais.

O componente torcida foi isolado pelo experimento natural dos jogos sem público:

| Fonte | Medida | Queda sem público |
| :--- | :--- | :--- |
| McCarrick et al. 2021 (4.844 jogos, 15 ligas) | diferencial mandante−visitante | +0,39 → +0,22 PPG = **−0,17** |
| Frontiers Behav. Econ. 2024 (6.864 jogos) | PPG do mandante | 1,592 → 1,509 = **−0,083** |
| PeerJ 2022 (temporada completa) | % de pontos em casa | 59,36% → 54,95% = **−4,41 pp** |

**O componente torcida vale 0,08 a 0,17 ponto por jogo.** Não a diferença de ~1 PPG entre a Nigéria e a Papua-Nova Guiné.

### 5.2 A decisão

**D-42 — `homeAdvantageModifier` é ordinal, expresso em pontos-por-jogo somados ao mandante, e cobre exclusivamente o componente torcida. Viagem, altitude e fatores institucionais são de outros sistemas e não entram neste parâmetro.**

| `AtmosphereArchetype` | `homeAdvantageModifier` | Ancoragem |
| :--- | ---: | :--- |
| `Apathetic` | 0,00 | Piso = jogo sem público (McCarrick) |
| `Corporate` | 0,05 | Abaixo do típico |
| `Traditional` | 0,11 | Típico, metade inferior (Frontiers 2024) |
| `Cauldron` | 0,17 | O efeito de torcida típico medido (McCarrick) |
| `Hostile` | 0,22 | Acima do típico — ver justificativa abaixo |

O teto de `Hostile` estende acima do típico com base em dois achados de mecanismo: o viés é **~4× maior em estádios sem pista de atletismo** (Dohmen 2008, 3.519 jogos) e cresce com a **densidade** da torcida, não com o número absoluto (Goumas 2014).

Isto mantém **D-06** intacto: atmosfera nunca toca a física. O parâmetro entra no resolvedor de partida, um consumidor só.

### 5.3 Achado sobre o mecanismo — insumo para o doc de Arbitragem

O canal mais bem documentado da torcida **não é o jogador, é o árbitro**:
- Amarelos do visitante caem 0,29 a 0,82 por jogo sem público (Reade & Singleton; Bryson et al. 2021).
- Viés de acréscimo quando o mandante está perdendo: +112,8 s na Espanha, +11,3 s no Brasil (survey IZA DP 8857).
- Endrich & Gesche 2020: o viés de faltas e cartões praticamente **sumiu** nos jogos sem público da Bundesliga.
- **Pênaltis e vermelhos: sem efeito significativo.**
- K League 2021 (público limitado a 10–30%): o efeito age mais **penalizando o visitante** do que beneficiando o mandante, e **satura cedo** — 10–30% de ocupação já produz quase todo o efeito.

Duas implicações que não são deste registro mas precisam chegar ao doc de Arbitragem: (a) se o modelo de árbitro vier a ler atmosfera, o efeito deve incidir sobre **cartão amarelo do visitante e acréscimo**, não sobre pênalti e vermelho; (b) modelar o efeito como **linear na ocupação é errado** — satura cedo e há evidência de relação em U.

---

## 6. D-43 — Constantes econômicas ajustadas contra alvos citáveis

**D-43 — `valueBase`, `valueDoublingStep`, `bandDecay` e `wageRateMonthly` são ajustados por mínimos quadrados contra alvos públicos, não escolhidos à mão.**

| Constante | Valor | Alvo que a fixa |
| :--- | ---: | :--- |
| `valueBase` | € 1.880.000 | Ajuste conjunto contra 4 alvos do Transfermarkt (28/01/2026) |
| `valueDoublingStep` | 4,7 OVR | idem |
| `bandDecay` | 0,90 por degrau | idem |
| `wageRateMonthly` | 0,0139 | **Média salarial da Série A = R$ 253.573/mês** (ANRESF + CBF Academy, Relatório de Transferências Brasil 1/2026) |
| `eurToBrl` | 6,195 | Câmbio implícito na conversão do Lance!, 28/01/2026 |

Resíduo do ajuste: jogador mais caro € 29,0 mi contra alvo € 35,0 · elenco topo € 253,1 mi contra € 209,9 · mediana € 86,8 mi contra € 92,3 · piso € 13,9 mi contra € 14,6. Média salarial R$ 254.144 contra R$ 253.573. Máximo R$ 2,50 mi/mês contra teto real de ~R$ 3,0 mi (Depay R$ 2,9 mi; Neymar a R$ 6 mi é outlier estrutural).

### 6.1 O resíduo que **não** foi eliminado, e por quê

A razão entre a folha do maior e a do menor clube saiu em ~17× contra ~9× real (Sports Value 2025: R$ 696 mi contra R$ 78 mi). Adicionar essa razão como quinto alvo **piora** o ajuste global: o otimizador empurra `bandDecay` para 1,00 (banda deixa de ter efeito) e o jogador mais caro cai para € 22,3 mi.

A tensão é diagnóstica, não um erro de ajuste: **o intervalo de OVR do lote é comprimido (39–79) contra a realidade.** Para produzir uma dispersão de valor de 14× a partir de uma dispersão de OVR estreita, a curva precisa ser muito convexa — e essa convexidade estoura a razão de folha. Os dois só fecham juntos quando a escala de OVR estiver certa.

**Ressalva de metodologia:** as folhas publicadas (Sports Value, Convocados) são *gastos com pessoal do departamento de futebol* — incluem comissão técnica, base e staff. O modelo cobre só jogadores. Parte dos 17× contra 9× é essa diferença de escopo, não erro de calibração.

### 6.2 Acoplamento declarado

`valuePivot = 62` está ancorado na **escala de OVR atual** (teto observado 79). **Corrigir o débito de calibração de OVR obriga a reajustar `valuePivot` no mesmo passo**, ou todos os valores de mercado sobem por um fator constante. Isso precisa entrar como precondição da tarefa de calibração de OVR, não ser descoberto depois.

**Não encontrado em fonte pública:** razão salário-anual/valor-de-mercado por jogador. CIES Football Observatory e Football Benchmark modelam valor sem usar salário como insumo. `wageRateMonthly` ficou ancorado pela média, que é dado duro, e não por uma razão, que não existe.

**Estado (rodada 2026-08-14): reajustado.** Ver D-46 §11.2 — `valuePivot` e `valueDoublingStep` foram refeitos por mínimos quadrados contra os mesmos 4 alvos, `valueBase` e `bandDecay` mantidos fixos.

---

## 7. D-44 — `defaultTacticalStyle` tem **duas proveniências**

A §5 lista `aiProfile.defaultTacticalStyle` como *derivado do estilo histórico documentado da âncora*. Na verificação dos 20 clubes, **9 não têm estilo histórico documentado em fonte alguma** — Corinthians, Grêmio, Cruzeiro, Bahia, Vitória, Coritiba, Chapecoense, Mirassol e Remo. O que existe para eles é identidade sociopolítica, formação de base ou narrativa de superação, nada tático.

Isto não é lacuna de pesquisa a preencher: historiografia tática é privilégio de clube grande. Em ligas periféricas a proporção será pior.

**D-44 — `defaultTacticalStyle` tem duas proveniências, registradas no campo `tacticalStyleProvenance`: `Derived` quando existe caracterização documentada (com a evidência citada no `deviationAudit`), e `Sampled` quando não existe — amostrado no stream determinístico, restrito por banda e `districtArchetype`.**

Não é fallback: é tabela de decisão sobre uma propriedade observável da entrada, mesma disciplina do seletor de `NamingRule` da §6.1. A proibição de fallback vale para recuperação de falha.

No lote: 11 `Derived`, 9 `Sampled`.

---

## 8. D-45 — Teste de três níveis substitui "não-marcária"

### 8.1 O problema

A condição 1 do seletor da §6.1 exige alcunha *"histórica documentada e **não-marcária**"*. Aplicada ao pé da letra sobre 20 clubes brasileiros verificados, ela quase nunca dispara — porque **toda alcunha viva aparece na comunicação oficial do clube**. É o que alcunha é.

Sem um critério observável, "não-marcária" vira julgamento, e o seletor deixa de ser determinístico.

### 8.2 A decisão

**D-45 — A perna "não-marcária" da condição 1 é substituída por um teste de três níveis, observável e verificável:**

| Nível | Definição | Evidência típica | `EpithetLift` |
| :--- | :--- | :--- | :--- |
| **0 — Marca de licenciamento** | Mascote registrado, linha de produtos, nome de loja ou canal oficial | "Urubão"/"Binho"; "Porco Gobbato"; "ShopTimão"; "Família Furacão"; "Vovô Coxa"; terceiro uniforme "Camisas Negras" | **PROIBIDO** |
| **1 — Uso institucional** | Aparece no hino, no site, na comunicação — sem produto, mascote ou loja | "Imortal" (hino de 1953); "Clube do Povo" (hino de 1957); "A Massa" (seção e Conselho no site); "Pó de Arroz" (matérias do clube) | **PERMITIDO**, com elemento geográfico novo acrescentado ao `officialName` |
| **2 — Folclore puro** | Só imprensa e torcida; o clube não usa | — | **PREFERENCIAL**, elevação literal aceitável |

A restrição do nível 1 (obrigar um elemento geográfico novo no nome oficial) é o que impede a elevação literal de uma alcunha que o clube usa institucionalmente. `Imortal Guaibense` e `Massa Mineira` já cumpriam; `Pó de Arroz` cumpre agora como `Pó de Arroz Carioca`.

### 8.3 Efeito medido

| Regra | v1 (julgamento) | v2 (D-45, verificado) |
| :--- | ---: | ---: |
| `RegionalCode` | 9 | **11** |
| `EpithetLift` | 6 | **4** |
| `Toponymic` | 4 | 4 |
| `Phonetic` | 1 | 1 |

Três clubes migraram de `EpithetLift` para `RegionalCode` ao serem verificados: **Flamengo** (mascotes oficiais Urubão e Binho), **Vasco** (Camisas Negras virou terceiro uniforme oficial do centenário) e — não — **Atlético-MG permaneceu**, porque "A Massa" é nível 1 enquanto "Galo" é nível 0, e o seletor testa a alcunha que vai ser elevada, não a mais famosa.

Distribuição de níveis no lote: 14 clubes em nível 0, 5 em nível 1, 1 em nível 2. **Confirma que o nível 0 é o caso comum no futebol brasileiro.** Em ligas europeias, onde o apelido de imprensa costuma ser menos licenciado, a distribuição deve inverter — vale medir no segundo lote antes de concluir qualquer coisa sobre a ordem da tabela da §6.1.

**Estado (rodada 2026-08-14): TESTADO. Ver §12 (D-47) — hipótese CONFIRMADA contra a Eredivisie holandesa.**

**Estado (rodada 2026-08-17): OPERACIONALIZADO. Ver §14.2 (D-51) — os três níveis viram a perna 4 de um teste de quatro pernas, já implementado e travado.**

---

## 9. O que continua aberto, e por quê

| Item | Estado | Por quê não foi resolvido aqui |
| :--- | :--- | :--- |
| **§11 #3 — limiar de ΔE** | Bloqueado | Calibração de *game feel* contra o renderizador (D-04), que não existe. Fica em 25,0; os 20 kits passam com folga (mínimo observado 73,4), então hoje não é o gargalo. |
| **Débito de OVR (79 vs 86–88)** | **RESOLVIDO** (D-46 + D-49) | Teto 88. O resíduo de 85 que a rodada 2 atribuiu a "amostra finita" era, na verdade, o bug do goleiro — ver §13.1. |
| **Tabelas de `CapProfile` por posição** | **Fórmula RESOLVIDA** (D-50) — aterrissagem aberta | A fórmula está travada em §14.1. O que falta não é decisão, é código: a camada de criação de personagem não existe ainda (atrás de D-07), e aplicar os tetos ao lote de 688 já gerado exige o refit de `valuePivot` do mesmo acoplamento de D-46/D-49. |
| **Partição dos 12 atributos** | **RESOLVIDO** (D-52) | Trava o que já era estrutural em `ATTR_GROUP`, e fecha a lacuna da curva de declínio do grupo `Goalkeeping` (entra em `TechDecay`). |
| **Regra de apelido — 4 pernas (limiar de 50 anos + fonte de `band`/`clubStrength`)** | **RESOLVIDO** (D-51) | Formaliza o que já estava implementado em `tp_import.py` e testado 20/20. Débito descoberto, não fechado aqui: o lote de 20 clubes não tem `bandSource`/`strengthSource` preenchidos — backfill é tarefa de pesquisa nomeada. |
| **B5 na tabela de capacidade** | **RESOLVIDO** (rodada 2026-08-14) | Thai League 1 2024-25 (n=16) — ver §11.1. |
| **`Mirassol` — `anchorFactsVerified`** | **RESOLVIDO** (rodada 2026-08-14) | Laudo oficial FPF localiza o estádio no Centro da cidade — ver §11.1. |

---

## 10. O que isto destrava

As três dependências de dados da §11 saem de "número inventado ou ausente" para "número com origem citada" em duas de três. O gate `anchorFactsVerified` da §3.1 passa de 0% para 95%, o que torna o lote **elegível a produção** pela primeira vez.

E dois erros de arquitetura que só apareceriam meses depois foram fechados agora: a circularidade de banda clube↔competição (D-38) e a derivação de capacidade que produzia mundos incoerentes (D-39). O segundo só apareceu porque o lote foi rodado com a regra literal antes de ser aceito — **vale manter essa disciplina: rodar a regra, olhar a saída, decidir depois.**

**A prioridade de implementação não muda.** Isto é camada de autoria e continua atrás de **D-07** (`SplitMix64 → física headless → steering mínimo → primeiro sprite`). A exceção é o patch de `PositionWeights.Overall` (D-40), que é uma linha e deve entrar antes de qualquer harness comparar planilha com engine.

---

## 11. D-46 — Débito de calibração de OVR: recalibrado, não fechado

### 11.1 Limpezas pontuais que entraram na mesma rodada

Antes de tocar em OVR, dois itens de dado puro (não de arquitetura) foram fechados por pesquisa dirigida:

- **Capacidade B5.** Nenhuma fonte agregada existia para "nacionais fracas/emergentes" (§11 #2 da rodada 1). Pesquisa encontrou a **Thai League 1 2024-25** (n=16 estádios, Wikipedia): média 12.290, desvio 8.360, faixa 5.000–32.600. **Achado que reabre o padrão de D-39:** essa média fica **abaixo de B6** (18.138, 3.Liga alemã) — a banda "nacional fraca" tem estádios menores que a quarta divisão profissional alemã. Não é erro de amostra: infraestrutura mede o país que constrói o estádio, não a força esportiva da liga (mesma lógica de D-39 §2.1). A tabela `BAND_CAPACITY["B5"]` foi atualizada com o dado real e a inversão foi documentada, não corrigida — corrigi-la reabriria D-39, que já decidiu que banda não é preditor de capacidade.
- **`Mirassol.anchorFactsVerified`.** Único clube dos 20 sem fonte sobre o caráter urbano do entorno do estádio. O **Laudo de Segurança oficial do estádio, protocolado na FPF**, situa o Estádio José Maria de Campos Maia no bairro Nossa Senhora Aparecida, "Centro da Cidade de Mirassol" — corroborado por banco de imagens e por guia de bairros (renda mista, comercial/residencial). `districtArchetype` corrigido de `Outskirts` para `HistoricCenter`. Contexto secundário registrado mas não usado para o rótulo: Mirassol é polo moveleiro nacional (indústria ~30% do PIB municipal contra ~1,8% de agricultura, FIESP) — é característica da cidade, não do quarteirão do estádio. `anchorFactsVerified` passa a `True`; o gate §3.1 fecha em **20 de 20**.

### 11.2 O débito de OVR

**Problema.** O lote real (688 jogadores) tinha teto de OVR 79 contra um alvo de produto de 86-88 (D-43 §6.2 já registrava o acoplamento com `valuePivot`).

**Pesquisa.** FBref e StatsBomb medem sempre por percentil relativo à população escolhida — não têm (nem precisam ter) um teto fixo comparável entre ligas, então não respondem à pergunta diretamente. A resposta veio de engenharia reversa do rating do EA FC (exemplo documentado de Van Dijk: 87,66 → 88 a partir da soma ponderada pura, sem bônus de reputação) e de uma âncora empírica dura: **o teto real do Brasileirão em rating publicado é mais conservador que o alvo do produto** — Hulk, o maior nome recente da liga, chegou a 84 de pico fora do país e ficou em 78-80 jogando no Brasil (EA Sports FC 26 / histórico FUTBIN-FUTWIZ). O mecanismo do EA FC não é "inflar tudo": é concentração de excelência nos atributos de **maior peso** da posição, com os de peso baixo ficando medianos e quase não puxando a média para baixo.

**Decisão — D-46.** Os atributos de maior peso por posição (`POSITION_WEIGHTS`) recebem: (a) as médias de arquétipo (`ARCH_MEANS`) sobem em degrau pelo rank de peso; (b) o desvio-padrão do ruído correlacionado sobe em degrau pelo mesmo rank. O teto do `leagueTier` mapeado (`club_tier` em `tp_build.py`) sobe de 0,98 para 1,00 — o cap anterior descontava ~2% do teto à toa mesmo para o clube de banda mais alta na liga mais forte.

**Verificação por consequência (duas rodadas, mesma disciplina de D-39).** A primeira tentativa bonificou só os **2** atributos de maior peso (+7/+8 na média, +10% de desvio no ruído) e foi **rodada contra a amostra real antes de ser aceita**. Resultado: o teto não passou de 84, porque o atributo de maior peso de cada posição batia no clamp de 99 sozinho (ex.: `Finishing` de atacante chegando a 99) enquanto os 2 atributos de maior peso costumam estar em **grupos diferentes** (ex.: `Finishing`=Technical, `Positioning`=Mental, correlação inter-grupo só 0,10) — raramente picam juntos, e o resto da soma ponderada (5-7 atributos ainda na média antiga) não empurrava o suficiente. Bonificar só 2 atributos desperdiçava headroom no clamp em vez de espalhar o ganho pela soma ponderada.

A correção estende o boost aos **4** atributos de maior peso por posição, em degrau (+10/+8/+5/+3 na média; +25%/+20%/+15%/+10% de desvio no ruído, pelo rank), calculado programaticamente a partir de `POSITION_WEIGHTS` (não hardcoded — evita duas fontes de verdade sobre "quais atributos importam"). Resultado: teto real da amostra de 688 sobe de 79 para **85**, com múltiplos jogadores de posições diferentes (CB, CM, DM) alcançando a faixa alta simultaneamente — não mais um único arquétipo isolado no clamp.

**Resíduo declarado, não forçado.** 85 ainda não é 86-88. Simulação de Monte Carlo sobre a fórmula (tier=1, idade de pico, sem clamp de amostra) mostra que a cauda teórica da distribuição já suporta 88-93 — o resíduo é efeito de **amostra finita** (688 jogadores, poucos titulares de banda B1 em idade de pico simultaneamente), não da forma da curva. Insistir em fechar exatamente 86-88 nesta amostra específica viraria ajuste de RNG, não calibração — decisão consciente de parar aqui e documentar o resíduo em vez de mascará-lo com mais uma rodada de tuning.

**Acoplamento fechado (D-43 §6.2).** `valuePivot` e `valueDoublingStep` foram re-fitados por mínimos quadrados contra os mesmos 4 alvos do Transfermarkt (jogador mais caro € 35 mi · elenco topo € 209,9 mi · mediana € 92,3 mi · piso € 14,6 mi), com `valueBase` e `bandDecay` mantidos fixos nos valores de D-43 (decay preservado deliberadamente perto de 0,90 — a busca em grade convergia para ~0,94-0,96 se livre, o que erode o efeito de banda, o mesmo problema que D-43 §6.1 já tinha rejeitado). Novos valores: `valuePivot` 62 → **67**, `valueDoublingStep` 4,7 → **5,2**. Resíduo pós-refit é da mesma ordem do documentado em D-43 §6.1 (jogador mais caro € 26,4 mi contra € 35 mi) — a tensão residual continua sendo a mesma diagnosticada lá (dispersão de valor de mercado precisa de uma curva convexa que a dispersão de OVR, mesmo maior agora, ainda não sustenta plenamente).

**O que isto NÃO fecha.** O debate sobre se o teto de produto deveria ser 86-88 ou algo mais próximo do precedente real (78-84, conforme Hulk) é decisão de design, não de pesquisa — sinalizado e deixado para quem decide o "quão suculenta" a liga deve parecer, não resolvido aqui por fiat.

---

## 12. D-47 — Hipótese D-45 testada contra a Eredivisie: **confirmada**

### 12.1 Escolha da segunda liga

D-45 §8.3 registrou uma hipótese não testada: "em ligas europeias, onde o apelido de imprensa tende a ser menos licenciado que no Brasil, a distribuição de `NamingRule` deveria inverter — mais `EpithetLift`, menos `RegionalCode`". Pesquisa dirigida avaliou 4 candidatas (Primeira Liga portuguesa, Bundesliga, Serie A italiana, Eredivisie) pelo critério que a hipótese exige: cultura de apelido **não-comercializada**. Português e alemão foram descartados de cara — mascote vivo oficial ligado ao apelido de imprensa é a norma (águia real "Vitória" do Benfica; bode "Hennes" do Colônia), o mesmo padrão de licenciamento que o Brasil tem. A Serie A ficou no meio do caminho (Lazio/Roma têm mascote vivo oficial, mas nem todos). **A Eredivisie holandesa foi escolhida**: nenhum clube pesquisado tem mascote vivo oficial ligado ao apelido histórico de torcida, e onde existe mascote oficial (Ajax "Lucky Lynx", AZ "Victor") é personagem de marketing recente sem relação com o apelido — exatamente o oposto estrutural do padrão brasileiro verificado em D-45.

### 12.2 O teste

Os 18 clubes da Eredivisie 2025-26 foram levantados (apelido principal, evidência de uso comercial oficial, toponímia no nome, cidade) e o **seletor real de `NamingRule` (§6.1) foi rodado sobre o dado**, não estimado por julgamento — mesma disciplina de D-39/D-45.

| Regra | Brasileirão (D-45, 20 clubes) | Eredivisie (D-47, 18 clubes) |
| :--- | ---: | ---: |
| `EpithetLift` | 4 (20%) | **12 (67%)** |
| `RegionalCode` | 11 (55%) | **0 (0%)** |
| `Toponymic` | 4 (20%) | 5 (28%) |
| `Phonetic` | 1 (5%) | 1 (6%) |

**A hipótese se confirma, e de forma mais decisiva do que a formulação original previa.** Não é só um deslocamento de proporção — `RegionalCode` **zera**. A condição 2 do seletor (colisão de múltiplos clubes na mesma cidade) só existiria em Roterdã (Excelsior, Feyenoord, Sparta) entre os 18, mas os três já resolvem antes, na condição 1 — todos têm apelido de nível 1 ou 2. Sem clubes-satélite disputando `RegionalCode` por colisão de cidade, a regra fica sem gatilho nesta liga inteira.

**Mecanismo, não só direção.** A leitura ingênua ("apelido europeu é menos licenciado, logo mais `EpithetLift`") está certa na direção mas incompleta no mecanismo: **um quarto dos clubes holandeses (5 de 18: Fortuna Sittard, Heerenveen, Heracles Almelo, NEC, PEC Zwolle) não têm apelido de imprensa distinto para elevar** — o nome do clube já contém o topônimo (`Sportclub Heerenveen`, `NEC` = Nijmegen Eendracht Combinatie) e cai direto em `Toponymic`, sem nunca chegar a testar o nível de licenciamento. Isso não existe no lote brasileiro, onde todo clube tem alcunha de imprensa candidata. Achado adicional não previsto por D-45: `Go Ahead Eagles` é um caso-limite — "Eagles" é apelido de imprensa de 1902 que virou parte do **nome oficial legal** na fusão de 1971, então não há mais um apelido separado do nome a "elevar"; o seletor cai em `Phonetic` por ausência de candidato, não por reprovação de nível.

**Consequência para a §6.1.** A condição 1 (`EpithetLift`) precisa checar **existência de apelido elevável distinto do nome oficial** antes de checar o nível de licenciamento — um clube cujo apelido histórico já foi incorporado ao nome legal (`Go Ahead Eagles`) ou cujo nome já é o topônimo (`Heracles Almelo`) não tem o que elevar. Isto não muda a ordem das 4 regras, só precisa a pré-condição da primeira. Registrar como ajuste de implementação quando o seletor for codificado (ainda não é — está atrás de D-07, como o resto desta camada).

**Escopo desta rodada.** O teste foi feito na **camada de identidade de clube** (nome, apelido, `NamingRule`, evidência) — não foi produzido um lote de jogadores/planilha completa para a Eredivisie. O objetivo desta tarefa era testar a hipótese registrada em D-45 §8.3, e isso não exige elenco: exige o seletor rodado contra apelidos reais verificados. Gerar cores, escudos, capacidades e 500+ jogadores fictícios holandeses é trabalho novo do mesmo tamanho do lote 1 inteiro — não decorre de "testar uma hipótese" e fica como tarefa nomeada separada, a disparar sob pedido explícito.

### 12.3 Tabela de trabalho — os 18 clubes e a regra aplicada

| Clube | Cidade | Apelido testado | Nível D-45 | Regra resultante |
| :--- | :--- | :--- | :---: | :--- |
| AFC Ajax | Amsterdã | Godenzonen | 2 | `EpithetLift` |
| AZ (Alkmaar Zaanstreek) | Alkmaar | De Kaasboeren | 2 | `EpithetLift` |
| SBV Excelsior | Roterdã | Oud Papier-club / Roodzwarten | 2 | `EpithetLift` |
| Feyenoord Rotterdam | Roterdã | De Trots van Zuid | 1 | `EpithetLift` |
| Fortuna Sittard | Sittard-Geleen | — (sem candidato) | — | `Toponymic` ("Sittard" no nome) |
| Go Ahead Eagles | Deventer | "Eagles" já é nome oficial (fusão 1971) | — | `Phonetic` (terminal — sem candidato, sem topônimo) |
| FC Groningen | Groningen | Trots van het Noorden | 1 | `EpithetLift` |
| Sportclub Heerenveen | Heerenveen | — (símbolo frísio, não apelido de clube) | — | `Toponymic` ("Heerenveen" no nome) |
| Heracles Almelo | Almelo | — (gentílico do próprio nome) | — | `Toponymic` ("Almelo" no nome) |
| NAC Breda | Breda | Parel van het Zuiden | 1 | `EpithetLift` |
| NEC (Nijmegen Eendracht Combinatie) | Nijmegen | — (sigla histórica) | — | `Toponymic` ("Nijmegen" no nome) |
| PEC Zwolle | Zwolle | — (sigla histórica) | — | `Toponymic` ("Zwolle" no nome) |
| PSV Eindhoven | Eindhoven | De Boeren | 2 | `EpithetLift` |
| Sparta Rotterdam | Roterdã | De Rood-Witte Gladiatoren | 2 | `EpithetLift` |
| SC Telstar | Velsen-Zuid | De Witte Leeuwen | 2 | `EpithetLift` |
| FC Twente | Enschede | Trots van het Oosten | 1 | `EpithetLift` |
| FC Utrecht | Utrecht | Domstedelingen | 2 | `EpithetLift` |
| FC Volendam | Volendam | De Palingboeren | 1 | `EpithetLift` |

Nenhum clube caiu em `RegionalCode`: os 3 clubes de Roterdã (a única cidade com mais de uma âncora entre os 18) resolvem todos na condição 1, antes de a condição 2 (colisão de cidade) ser avaliada.

**Fontes principais:** Wikipedia em inglês por clube; [doordebenen.nl — Bijnamen van voetbalclubs](https://www.doordebenen.nl/bijnamen-voetbalclubs-landenteams/); site oficial AZ ([az.nl/victor](https://www.az.nl/victor)); página do Feyenoord sobre "De Trots van Zuid".

---

## 13. D-48 e D-49 — A ponta solta do ledger, e o bug que ela escondia

### 13.1 A partição dos 12 nunca foi decidida — mas já era estrutural

O §9 listava dois itens de ledger em aberto: as tabelas de `CapProfile` por posição e a **partição dos 12 atributos** em `Physical`/`Technical`/`Mental`. São um item só: as tabelas de cap dependem da partição.

O que a rodada 1 não notou é que **a partição já estava carregando peso**. O `ATTR_GROUP` do gerador alimenta a construção da matriz de correlação — 0,35 dentro do grupo, 0,10 fora — e é ela que impede o goleiro-driblador. Ou seja: uma decisão que ninguém tomou moldou os 688 jogadores inteiros.

E com uma divergência silenciosa. Os docs falam em **três** grupos, com *"o tratamento de goleiro dentro dos 12"* marcado como aberto no ledger. O código resolveu por conta própria criando um **quarto** grupo (`Goalkeeping`). Não é detalhe: a §9.4 da *Criação de Personagem* diz que `PhysDecay` incide no subconjunto físico e `TechDecay` "no resto" — com um quarto grupo, "o resto" é ambíguo, e `Reflexes`/`Handling` não têm regra de declínio definida. Goleiro envelhece bem no mundo real, então a resposta muda carreira.

**D-48 — A partição dos 12 é reconhecida como decisão estrutural já em vigor, não como pendência abstrata. O quarto grupo `Goalkeeping` fica travado como parte dela (§13.3 fecha a perna do goleiro). A partição `Physical`/`Technical`/`Mental` das outras dez continua aberta, mas agora declarada como *em uso*, não como *ausente* — qualquer mudança nela invalida a covariância e obriga a regerar o lote.**

### 13.2 O bug

Puxar essa ponta expôs um defeito de produção. No gerador (porta Python do `SimCore.cs`), o teste de papel cruzado rodava **antes** da consulta ao arquétipo:

```python
cross_role = (a in GK_ATTRS) != (position == "GK")   # ERRADO: vem antes
if cross_role:
    out[a] = rng.next_int(15, 36); continue
mu = means.get(a)                                     # nunca alcançado para GK
```

Para um goleiro, `(a in GK_ATTRS) != (position == "GK")` é **verdadeiro em todo atributo que não seja de goleiro**. Resultado: os 73 GKs do lote saíram com `Positioning` e `Composure` sorteados em 15–36 em vez dos 77/73 que o arquétipo prescreve — **35% do peso do OVR de goleiro virando ruído**, sem nada estourar. O melhor goleiro da liga era 72 contra 85 do melhor jogador de linha.

O `SimCore.cs` **não** tem o bug: ele faz `if (attrs.ContainsKey(a)) continue;` primeiro, e o laço de papel cruzado só vê atributos fora do arquétipo. A porta inverteu a ordem. Corrigido, e o C# ganhou um comentário no ponto exato para quem portar de novo.

**Consequência retroativa:** o resíduo que a §11.2 atribuiu a "amostra finita" (teto 85 contra alvo 86–88) era, em boa parte, este bug. Corrigido, o teto vai a **88** — dentro do alvo, sem nenhuma calibração adicional. A explicação da rodada 2 estava errada, e fica registrada como errada.

### 13.3 D-49 — o vetor de OVR do goleiro passa de 4 para 7 atributos

Corrigido o bug, apareceu o que ele mascarava: **goleiro máximo 95 contra 85 do melhor jogador de linha**.

Duas causas, ambas estruturais:

**(a) O lift de D-46 não era invariante ao tamanho do vetor.** A regra era "some +10/+8/+5/+3 aos 4 atributos de maior peso". Como rank ignora *quanto* peso cada atributo carrega, o lift saía diferente por posição — e o goleiro, que tinha **exatamente 4** atributos, teve "top-4" igual a "todos" e levou +7,35 contra +5,1 a +5,8 das outras sete. Antes de D-46 as oito estavam equilibradas em 72–75.

**(b) Um vetor de 4 atributos tem cauda mais gorda.** Σw² = 0,275 contra 0,17–0,19 das demais: menos termos fazem menos média. O desvio do somatório ponderado era 5,42 contra ~4,5. Isso não é bug — é propriedade de `POSITION_WEIGHTS`.

**D-49 — O vetor de OVR do goleiro passa a ter 7 atributos: `Reflexes` 0,26 · `Handling` 0,20 · `Positioning` 0,15 · `Passing` 0,13 · `Pace` 0,10 · `Composure` 0,09 · `Vision` 0,07.**

Ancoragem, não gosto: o **EA FC 26** avalia goleiro por *seis* atributos — Diving, Handling, **Kicking**, Reflexes, **Speed**, Positioning. E o teste empírico do **FM-Arena** (900 jogos, +10 por atributo isolado, erro padrão ~1,1) mediu o impacto real sobre gols sofridos e rankeou **Pace em 2º e Vision em 3º, acima de Reflexes**. Mapeando para os 12 canônicos: Kicking → `Passing`, Speed → `Pace`. Distribuição e leitura são o miolo do goleiro moderno, não adorno — e as médias de base do arquétipo acompanharam (`Passing` 52→70, `Vision` 48→66, `Pace` 48→62; um goleiro de `Passing` 52 é um goleiro de 1995).

Σw² cai para 0,170 e o desvio para 4,42, ambos dentro da faixa das outras posições. **Isto fecha a perna "tratamento de goleiro dentro dos 12" do item de ledger.**

**D-49b — O lift de D-46 deixa de ser tabela e vira fórmula:**

```
bônus(a) = k_pos × w(a),   com   k_pos = TARGET_LIFT / Σ w(a)²   sobre os top-N
TARGET_LIFT = 5,5 pontos de OVR · BOOSTED_COUNT = 4
```

O lift fica invariante ao tamanho do vetor por construção — nenhuma posição futura pode repetir o acidente do goleiro. As `ARCH_MEANS` deixam de ser tabela hard-coded e passam a ser calculadas a partir de `ARCH_MEANS_BASE` + fórmula, o que elimina a duplicação entre "médias de design" e "médias pós-lift".

**Resultado medido no lote:** lift uniforme entre +5,36 e +5,63 nas oito posições (era +5,12 a +7,35); base 77,9–80,3; desvio do ruído 4,34–4,60. OVR geral: mínimo 42, **máximo 88**, média 68,5. Por posição, máximos entre 81 e 88 — sem nenhuma posição isolada no topo.

**Reajuste econômico obrigatório (mesmo acoplamento de D-43 §6.2 e D-46).** `valuePivot` 67 → **68** e `valueDoublingStep` 5,2 → **5,1**, re-fitados contra os mesmos 4 alvos do Transfermarkt. Elenco mediano € 91,6 mi contra alvo € 92,3 mi (−0,7%) e salário médio R$ 261 mil contra R$ 254 mil (+3%) — os dois melhores resíduos já obtidos. Jogador mais caro (€ 24,1 mi contra € 35 mi) e elenco topo (€ 260 mi contra € 210 mi) continuam com a tensão diagnosticada em D-43 §6.1, que é de forma da curva, não de escala.

**Ordem deliberada:** o refit econômico foi feito **depois** da correção do goleiro, não antes. Com o bug de pé, o jogador mais caro do lote era o goleiro artefato de 95 (€ 48,7 mi), e ajustar as constantes contra ele teria lavado o defeito para dentro da calibração — o mesmo erro que D-43 §6.1 recusou cometer ao rejeitar o quinto alvo.

### 13.4 Achado colateral: vazamento de IP no pool de regen

O gate de higiene do exportador (novo nesta rodada) reprovou o build: `SURNAME_POOL`, o pool de sobrenomes dos jogadores sem âncora, continha **Duarte, Galvão, Monteiro, Tavares e Xavier** — sobrenomes de âncoras reais **da mesma liga**. Um regen podia nascer "Bruno Xavier" ao lado do "Samuel Xavier" real. O desvio fonético nunca protegeu contra isso porque regen não passa por ele. Corrigido na raiz: o pool subtrai os sobrenomes reais do lote antes de sortear.

O gate compara **sobrenome**, não nome completo: o primeiro nome vem de um pool genérico por nacionalidade e colide com nome real por ser comum ("Bruno", "Diego"), não por vazar. A primeira versão do gate comparava tudo, acusou 79 falsos positivos e teria treinado a equipe a ignorar o alarme — que é o pior estado possível para um gate.

### 13.5 O que continua aberto

**Estado (rodada 2026-08-17): ver §14.** `CapProfile` tinha a fórmula em aberto — está travada agora em D-50. O acoplamento abaixo continua valendo tal como descrito; o que mudou é que a fórmula que vai gerar o refit já está definida, não é mais incógnita.

Nos slots de maior peso, **5,5% dos valores gerados estão em 90 ou acima**, que é exatamente a faixa onde `cap = base(posição, atributo) × modificador(altura, físico)` morde. Aterrissar `CapProfile` pode desfazer parte do lift de D-46/D-49 e obrigar a um terceiro refit econômico.

Precondição declarada, para não ser descoberta depois: **`CapProfile` entra junto com um refit de `valuePivot`, e o teto de OVR precisa ser re-medido no mesmo passo.** É o mesmo padrão de acoplamento de D-43 §6.2 → D-46 → D-49, agora conhecido de antemão.

---

## 14. D-50, D-51, D-52 — as três pontas do ledger, decididas

> **Nota de sessão (2026-08-17, rodada 4).** Fecha os três itens que a rodada 3 deixou como "o único item de fundo em aberto" (§13.5) mais os dois sub-itens que D-48 declarou *em uso mas não travados* (§13.1). D-51 e D-52 formalizam o que já estava implementado em código sem pesquisa nova. D-50 é formulação nova, mas não aterrissada nesta rodada.

### 14.1 D-50 — `CapProfile`: fórmula travada, aterrissagem continua sob acoplamento declarado

**Problema.** `CapProfile = (position, height, buildType) → teto por atributo` estava travado como assinatura (T-04) desde a *Criação de Personagem*, mas "as tabelas concretas por posição não existem ainda" (§5.4 daquele doc). Sem números, o passo 4 do wizard de criação (`Attribute Allocation`) não é implementável, e o acoplamento declarado em §13.5 não tinha fórmula para gerar a remedição que promete.

**D-50 — `CapProfile` usa dois níveis de composição: uma base por posição/atributo em três degraus, ancorada no mesmo agrupamento canônico do lift de D-46/D-49 (evita uma segunda fonte de verdade sobre "quais atributos importam"), e um modificador físico que só toca os três atributos do grupo `Physical`.**

```
base(pos, attr):
  attr ∈ TOP4_ATTRS[pos]              → 99   # já existe, calculado a partir de POSITION_WEIGHTS
  attr ∈ POSITION_WEIGHTS[pos], resto → 85
  attr fora de POSITION_WEIGHTS[pos]  → 60   # trava o goleiro-driblador na criação, não só na covariância

buildFactor(attr, buildType):     # só Pace/Strength/Stamina; 1,0 no resto
  Lean:      Pace ×1,08  Strength ×0,85  Stamina ×1,00
  Balanced:  Pace ×1,00  Strength ×1,00  Stamina ×1,00
  Athletic:  Pace ×1,05  Strength ×1,05  Stamina ×0,92   # sem almoço grátis: ganha nos dois eixos, perde em fôlego
  Stocky:    Pace ×0,85  Strength ×1,08  Stamina ×1,00

heightFactor(attr, height):       # só Strength/Pace; linear, 182cm = neutro, faixa 160–205
  Strength: 1,0 + 0,10 × (height − 182) / 23
  Pace:     1,0 − 0,06 × (height − 182) / 23

cap(pos, attr, height, buildType) = round(base × buildFactor × heightFactor), clamp [1, 99]
```

Justificativa dos degraus: 99 nos 4 atributos-assinatura preserva a possibilidade de especialização plena (um zagueiro pode chegar a 99 em `Tackling`); 85 no resto do arquétipo permite competência sem permitir dominância fora de posição; 60 fora do arquétipo é o que impede o "goleiro-driblador" na criação — a covariância (0,10 entre grupos) já desencoraja isso na geração de NPC, mas não impedia um jogador humano de alocar `AP` livremente num atributo irrelevante para a posição.

Justificativa do `buildFactor`: nenhum build domina os outros em todos os eixos — `Athletic` ganha pico em `Pace` e `Strength` ao custo de `Stamina`, para preservar a tensão "alto-e-forte × baixo-e-rápido" que o doc original pede, sem criar uma escolha estritamente superior às outras três.

**Consequência que não estava prevista.** Como o teto do grupo top-4 é 99 — o mesmo teto absoluto do sistema —, ele não morde a maioria dos 5,5% de valores 90+ da amostra atual (a maioria já está num atributo-assinatura). O que morde é o teto de 85 nos atributos fora do top-4: qualquer jogador do lote atual com um atributo secundário acima de 85 por efeito de ruído correlacionado precisaria ser clipado quando `CapProfile` aterrissar. Isso é mais restrito do que a estimativa original de "5,5% em risco" sugeria (nem todo esse percentual está em risco de fato) — e mais barato de absorver no refit econômico do que se temia.

**O que esta decisão NÃO faz.** Não aplica os tetos ao lote de 688 já gerado, e não dispara o refit de `valuePivot`. `CapProfile` é mecânica de **criação de personagem** (wizard do jogador, passo 4) — não existe ainda em código, porque toda essa camada está atrás de **D-07** (mesma prioridade que já valia antes). A fórmula acima é o que entra quando essa camada for construída. O acoplamento declarado em §13.5 continua valendo tal como escrito: aterrissar exige remedir o teto de OVR e refitar `valuePivot` no mesmo passo — só que agora a fórmula que vai gerar essa remedição já está definida, não é mais incógnita.

Ancoragem: nenhuma — é constante de *game feel*, mesma categoria da janela de Jaro-Winkler (§6.2 do *Geração de Identidade*) e do corte de 50 anos do D-51 abaixo. Exposta como constante nomeada para ajuste, não como resultado de pesquisa — não existe pesquisa externa sobre "quanto um build alto deveria pesar num teto de atributo de um jogo".

### 14.2 D-51 — Regra de apelido de 4 pernas: travada como já estava implementada

**Problema.** `tp_import.py` já implementa as quatro pernas da condição 1 (ano ≥ 50 anos documentado · não-descritiva · elevável (D-47) · nível ≥ 1) e já **exige fonte citável** (`bandSource`, `strengthSource`) para `band`/`clubStrength` no gate `gate_anchor_facts_verified`. Rodado contra os 20 clubes reais, reproduz **20/20**. Mas nada disso tinha virado decisão travada — o código citava "D-48" num comentário, número que a §13 deste registro usou depois para outro assunto (a partição dos 12). Chamar dois assuntos diferentes de D-48 é exatamente o tipo de furo que a nota de numeração no cabeçalho existe para evitar.

**D-51 — As quatro pernas de `tp_import.py` (`nickname_passes_condition_1`) são a condição 1 travada do seletor de `NamingRule` (§6.1 da *Geração de Identidade v2*), com o corte da perna 1 mantido em 50 anos. `bandSource` e `strengthSource` continuam obrigatórios em toda submissão terceirizada — não são substituídos por um requisito mais fraco.**

Justificativa do corte de 50 anos: é uma constante de *design*, não um fato pesquisável — não existe padrão externo sobre "quantos anos uma alcunha precisa ter para ser folclore". Mantê-lo em vez de reabrir é a escolha certa porque (a) já reproduz o corpus verificado 20/20 sem nenhum caso de fronteira sensível ao valor exato — os clubes que falham a perna 1 falham por não terem `nicknameOriginYear` documentado, não por estarem perto do corte; e (b) meio século casa com a linguagem que o resto do registro já usa para "histórico" (hino de 1953, hino de 1957, centenário do Vasco) — trocar por outro número não muda nenhum resultado observado e criaria uma segunda constante sem uso real.

Justificativa de manter a exigência de fonte para `band`/`clubStrength`: a objeção óbvia é "banda é fato autorado (D-38), não deveria precisar de fonte, como `cityName`". Isto confunde duas coisas — D-38 decidiu que a banda **não é derivada mecanicamente** da competição (direção clube→competição, para quebrar a circularidade). Não decidiu que a banda é inventável sem âncora. Todo clube deste pipeline é gerado por desvio de um clube REAL (`ReferenceAnchor`) que **tem** posição competitiva real citável — então exigir `bandSource` é a mesma disciplina de ancoragem que já vale para capacidade (D-39), constantes econômicas (D-43) e citação de escudo (§3), não uma regra nova. Relaxar a exigência porque o lote atual dos 20 clubes reprova nela repetiria exatamente o erro que D-39 recusou cometer com a capacidade da B5: esconder um buraco em vez de marcá-lo.

**Consequência que não estava prevista.** Sob esse contrato, **os 20 clubes do lote atual reprovam** em `bandSource`/`strengthSource` — nenhum foi autorado com citação no momento em que a banda foi atribuída. Isto não é regressão desta decisão: é o gate expondo uma dívida que já existia, exatamente como `anchorFactsVerified` expôs a falta de `districtSource` antes de D-46 §11.1 corrigir Mirassol.

**Débito aberto por esta decisão (não fechado aqui).** Backfill de `bandSource`/`strengthSource` para os 20 clubes do lote 1, citando a posição competitiva real de cada âncora (coeficiente, tabela da temporada ou valor de elenco Transfermarkt). É trabalho de pesquisa limitado e nomeado — mesmo padrão de §11 #1/#2 do *Geração de Identidade v2* — não decisão de design, e não foi executado nesta rodada.

Ancoragem: nenhuma pesquisa nova; formaliza o que já estava implementado e testado (20/20) em `tp_import.py`. O comentário `# anos, D-48 perna 1` em `tp_import.py` foi corrigido para `# anos, D-51 perna 1`.

### 14.3 D-52 — Partição Physical/Technical/Mental travada; grupo `Goalkeeping` entra em `TechDecay`

**Problema.** D-48 (§13.1) reconheceu que `ATTR_GROUP` já particiona os 12 atributos em quatro grupos e já governa a matriz de correlação do gerador, mas deixou a partição das dez não-goleiro formalmente aberta, e não decidiu qual curva de declínio (`PhysDecay`/`TechDecay`) o grupo `Goalkeeping` (`Reflexes`, `Handling`) usa — a *Criação de Personagem* §9.4 só define as duas curvas para "físico" e "o resto", e o quarto grupo tornou "o resto" ambíguo.

**D-52 — A partição travada é a que já está em `ATTR_GROUP`: `Technical` = {Finishing, Passing, Dribbling, Tackling} · `Physical` = {Pace, Strength, Stamina} · `Mental` = {Positioning, Vision, Composure} · `Goalkeeping` = {Reflexes, Handling}. `PhysDecay` incide só sobre `Physical`. `TechDecay` incide sobre `Technical` + `Mental` + `Goalkeeping` — o grupo do goleiro entra na curva lenta, não numa terceira curva nova.**

Justificativa da partição: não é uma decisão nova, é o reconhecimento formal de uma decisão que os 688 jogadores do lote já obedecem — mudar qualquer atributo de grupo agora invalidaria a matriz de covariância (0,35 dentro do grupo, 0,10 fora) e obrigaria a regerar o lote inteiro, então travar como está é a opção sem custo.

Justificativa de `Goalkeeping` em `TechDecay`, não `PhysDecay` nem uma curva nova: goleiro profissional sustenta carreira de elite bem além dos 35-38 anos em que o físico de linha já caiu — é conhecimento de domínio já presente na literatura de futebol e consistente com o que o próprio D-49 já havia medido (o teste FM-Arena rankeou `Pace`/`Vision` acima de `Reflexes` em impacto sobre gols sofridos, ou seja, leitura de jogo pesa mais que reação física crua no desempenho de goleiro). `Reflexes`/`Handling`, apesar do nome, medem leitura de jogo e posicionamento tanto quanto tempo de reação — mais próximos do "resto técnico/mental" que `TechDecay` já modela (resiste até 44, então −1/temporada) do que do físico puro que degrada desde o Prime. Reaproveitar `TechDecay` em vez de inventar uma terceira curva é a opção que respeita *one way* — nenhum mecanismo novo, só uma associação de grupo que faltava.

**Consequência que não estava prevista.** Sem esta decisão, o goleiro do lote não tinha declínio nenhum definido para `Reflexes`/`Handling` — nem cedo (`PhysDecay`) nem tarde (`TechDecay`), um buraco silencioso que só apareceria quando o sistema de carreira fosse escrito. Fechar agora evita que alguém implemente por padrão o caminho errado (`PhysDecay`, por `Reflexes` "soar" físico) só porque o nome do atributo engana.

Ancoragem: nenhuma pesquisa nova — formaliza `ATTR_GROUP`, já em produção, e aplica uma inferência de domínio (curva de carreira de goleiro) sem número novo a calibrar (reaproveita os parâmetros já travados de `TechDecay`: resiste até 44, −1/temporada até 46).
