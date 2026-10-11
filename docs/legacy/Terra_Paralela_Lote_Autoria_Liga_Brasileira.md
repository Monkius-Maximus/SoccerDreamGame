# Terra Paralela — Lote de Autoria: Liga Brasileira (liga-piloto) — **v2**

> **Natureza.** Log do primeiro lote real do pipeline de autoria de mundo — passo 6 do build path da §12 do *Geração de Identidade de Clube v2*. Não é doc de design; é o registro do que foi produzido, com que regras, e com que fontes.
>
> **v2 substitui a v1.** A v1 saiu com `anchorFactsVerified = FALSO` em 100% das linhas e três dependências de dados por resolver. Esta revisão passou os 20 clubes por verificação em fonte, o que **refutou 9 afirmações** e mudou a regra de nomenclatura de 3 clubes. As decisões de arquitetura que o lote levantou estão em *Registro de Decisões: Calibração e Autoria de Mundo* (D-38…D-47) — este doc não as repete.
>
> **Rodada 2 (2026-08-14).** Recalibração de OVR (D-46), reajuste de `valuePivot`/`valueDoublingStep`, B5 e Mirassol resolvidos, e a hipótese D-45 testada contra a Eredivisie (D-47).
>
> **Rodada 3 (2026-08-15).** Corrigido um **bug de produção** que mantinha os 73 goleiros do lote com 35% do peso do OVR preso em ruído (D-49 §13.2); o vetor de OVR do goleiro foi reescrito de 4 para 7 atributos; o lift de D-46 virou fórmula; e o pool de regen deixou de colidir com sobrenomes reais. Números desta página refletem o estado pós-rodada 3.
>
> **Cross-ref:** *Geração de Identidade de Clube v2* · *Criação de Personagem e Ciclo de Vida* · *Registro de Decisões: Geografia e Competições* (D-30, D-31) · *Sistema de Prestígio de Competições* · *Registro de Decisões: Calibração e Autoria de Mundo* (D-38…D-47) · `SimCore.cs`.
>
> **Entregável:** `TerraParalela_Liga_Brasileira_BaseDeMundo.xlsx` — 11 abas, 20 clubes, 688 jogadores, 3.488 fórmulas, 0 erros de fórmula.
> **masterSeed:** `20260814`. Mesma seed → mesmo arquivo bit a bit.

---

## 1. Escopo

| | |
| :--- | :--- |
| Competição | `cmp_bra_tier1` · scope `National` · banda **B2** · 20 clubes · 38 rodadas |
| Clubes | 20 `ClubIdentity` + 20 linhas de `deviationAudit` **com URL por afirmação** |
| Jogadores | 688 `CharacterRecord` + 688 linhas de auditoria de desvio |
| Cobertura de âncora | 288 ancorados (41,9%) · 400 regen, marcados em `provenance` |
| GeoNode | 18 nós (World → CONMEBOL → Brasil → 4 regiões → 11 cidades) |
| `anchorFactsVerified` | **VERDADEIRO em 20 de 20** (Mirassol resolvido na rodada 2 — D-46 §11.1) |
| OVR (pós D-46 + D-49) | mín 42 · máx **88** — dentro do alvo 86-88 · média 68,5 · máximos por posição entre 81 e 88 |

Âncora de liga: primeira divisão brasileira 2026, 20 participantes confirmados em [fonte pública](https://en.wikipedia.org/wiki/2026_Campeonato_Brasileiro_S%C3%A9rie_A).

---

## 2. Mapa âncora → clube fictício

| Âncora real | Clube em Terra Paralela | Regra | Nível da alcunha | Banda | Estádio | Estilo | `homeAdv` |
| :--- | :--- | :--- | :--- | :--- | ---: | :--- | ---: |
| Clube de Regatas do Flamengo | **Sociedade Carioca Sul A** (CAR) | RegionalCode | 0 licenciamento | B2 | 27.000 | HighPress *(D)* | 0,17 |
| Sociedade Esportiva Palmeiras | **Sociedade Paulista Oeste A** (PAU) | RegionalCode | 0 licenciamento | B2 | 39.500 | Possession *(D)* | 0,17 |
| Sport Club Corinthians Paulista | **Sociedade Paulista Leste B** (PLB) | RegionalCode | 0 licenciamento | B2 | 44.500 | Counter *(S)* | 0,17 |
| São Paulo Futebol Clube | **Sociedade Paulista Sul C** (PSC) | RegionalCode | 1 institucional | B2 | 41.000 | Possession *(D)* | 0,11 |
| Grêmio Foot-Ball Porto-Alegrense | **Imortal Guaibense** (IMO) | EpithetLift | 1 institucional | B2 | 35.500 | Controlled *(S)* | 0,17 |
| Sport Club Internacional | **Sport Club Povo Gaúcho** (POV) | EpithetLift | 1 institucional | B2 | 35.000 | Direct *(D)* | 0,17 |
| Clube Atlético Mineiro | **Esporte Clube Massa Mineira** (MAS) | EpithetLift | 1 institucional | B2 | 22.000 | HighPress *(D)* | 0,17 |
| Cruzeiro Esporte Clube | **Sociedade Mineira Oeste A** (MIN) | RegionalCode | 0 licenciamento | B2 | 48.000 | Possession *(S)* | 0,17 |
| Botafogo de Futebol e Regatas | **Sociedade Carioca Norte B** (CNB) | RegionalCode | 0 licenciamento | B2 | 50.000 | Possession *(D)* | 0,11 |
| Fluminense Football Club | **Pó de Arroz Carioca** (POD) | EpithetLift | 1 institucional | B2 | 56.000 | Possession *(D)* | 0,11 |
| Club de Regatas Vasco da Gama | **Sociedade Carioca Central C** (CCC) | RegionalCode | 0 licenciamento | B3 | 62.000 | Direct *(D)* | 0,17 |
| Santos Futebol Clube | **Associação Atlética Baixada Litoral** (BAI) | Toponymic | 0 licenciamento | B3 | 23.500 | Possession *(D)* | 0,17 |
| Esporte Clube Bahia | **Sociedade Baiana Central A** (BCA) | RegionalCode | 0 licenciamento | B3 | 53.500 | HighPress *(S)* | 0,17 |
| Esporte Clube Vitória | **Sociedade Baiana Norte B** (BNB) | RegionalCode | 0 licenciamento | B4 | 18.500 | Direct *(S)* | 0,22 |
| Red Bull Bragantino | **Clube Atlético Mantiqueira** (MAN) | Toponymic | 0 licenciamento | B4 | 12.500 | Direct *(D)* | 0,05 |
| Mirassol Futebol Clube | **Associação Atlética Noroeste Paulista** (NOR) | Toponymic | 0 licenciamento | B4 | 20.000 | Counter *(S)* | 0,11 |
| Club Athletico Paranaense | **Sociedade Paranaense Sul A** (PAR) | RegionalCode | 0 licenciamento | B3 | 62.000 | HighPress *(D)* | 0,05 |
| Coritiba Foot Ball Club | **Sociedade Paranaense Norte B** (PNB) | RegionalCode | 0 licenciamento | B4 | 28.500 | LowBlock *(S)* | 0,11 |
| Associação Chapecoense de Futebol | **Sociedade Esportiva Oeste Catarinense** (OES) | Toponymic | 2 folclore | B5 | 36.500 | LowBlock *(S)* | 0,11 |
| Clube do Remo | **Clube Náutico Remolar** (REM) | Phonetic | 0 licenciamento | B5 | 42.500 | Direct *(S)* | 0,17 |

*(D)* = `tacticalStyleProvenance: Derived`, com evidência citada · *(S)* = `Sampled`, âncora sem estilo documentado (D-44).

**`clb_bra_poa_001` (Imortal Guaibense) continua sendo a instância de referência da §13 do doc v2**, com duas mudanças que a própria §13 autoriza (ela avisa que seus fatos não passaram pelo gate): `derbyRivalClubId` agora aponta para `clb_bra_poa_002`, que passou a existir; e `districtArchetype` corrigido de `WorkingClass` para `Affluent` — a sede histórica é Moinhos de Vento, bairro nobre, não zona industrial.

**Mirassol (NOR)**, resolvido na rodada 2: `districtArchetype` corrigido de `Outskirts` para `HistoricCenter` — laudo oficial FPF situa o estádio no Centro da cidade, bairro de renda mista (ver D-46 §11.1).

---

## 3. O que a verificação refutou

Nove afirmações da v1 caíram na checagem em fonte. Cada uma está corrigida no arquivo, com URL na aba `Audit_Clubes`.

| # | Afirmação da v1 | Veredito |
| :--- | :--- | :--- |
| 1 | Flamengo: "Urubu" é folclore não-marcário | **REFUTADO** — mascotes oficiais Urubão e Binho. Nível 0 → `EpithetLift` proibido |
| 2 | Vasco: "Camisas Negras" é folclore não-marcário | **REFUTADO** — virou terceiro uniforme oficial do centenário. Nível 0 |
| 3 | Bragantino: carga central = Pedra Grande | **REFUTADO** — a Pedra Grande fica em **Atibaia**, ~25 km. Substituída pelo Lago do Taboão |
| 4 | Bragantino: "Massa Bruta" é criação de marketing | **REFUTADO** — surge em **1929**, na imprensa local. (Continua nível 0 por outro motivo: domínio e canal oficiais) |
| 5 | Grêmio: sede em zona de caráter industrial | **REFUTADO** — Moinhos de Vento é residencial nobre |
| 6 | Atlético-MG: sede em zona operária | **REFUTADO** — Lourdes é residencial nobre; Pampulha é conjunto monumental planejado |
| 7 | Cruzeiro: Barro Preto é bairro operário/industrial | **REFUTADO** — é polo comercial de imigração italiana, pericentral |
| 8 | Coritiba: Alto da Glória é centro histórico | **REFUTADO** — é bairro nobre verticalizado |
| 9 | Vitória: "Leão da Barra" vem do bairro-sede | **REFUTADO** — vem do **remo**, das travessias saindo do Porto da Barra. Os fundadores eram do Corredor da Vitória |

Além disso, três correções de ano: São Paulo tem fundação (1930) **e refundação (1935)** — o ano gerado foi deslocado para não coincidir com nenhuma das duas; Botafogo é 1894 como clube de regatas, 1904 no futebol e 1942 na fusão; "Pó de Arroz" é de **1914**, não "anos 1910".

**Isto é o gate `anchorFactsVerified` fazendo exatamente o que foi desenhado para fazer.** Nove erros que teriam entrado no build e virado dívida de conteúdo.

---

## 4. Camada fonética — resultado empírico

288 desvios de sobrenome, **todos** na janela `0,55 ≤ Jaro-Winkler(Double Metaphone) ≤ 0,80`. Mínimo observado 0,583; máximo 0,800.

**270 automáticos · 18 por autoria manual (6,3%).** O padrão dos que estouram o teto e a decisão sobre o gate de pronunciabilidade estão em **D-41**.

---

## 5. Invariantes que o lote v2 passou

| # | Invariante | Origem |
| :--- | :--- | :--- |
| 1 | `displayCode` único global, 3 letras A–Z, pela cascata de 4 candidatos | §9.1 |
| 2 | `foundingYear` na década real e diferente do ano real nos 20 | §9.2 |
| 3 | 288/288 desvios fonéticos na janela; nenhum sobrenome gerado igual à âncora | §6.2 |
| 4 | ΔE(titular, reserva) ≥ limiar nos 20; reserva sem matiz novo | §7.2 |
| 5 | `capacity` dentro do perfil de estádio do país nos 20 | D-39 |
| 6 | Atributos em [1,99]; banda reservada 100–120 intocada | §2.4 / §5.1 |
| 7 | `potentialCeiling ≥ currentAttributes`, teto 99 | §3 |
| 8 | OVR da planilha idêntico ao do gerador nas 688 linhas | D-40 |
| 9 | Camisas únicas por elenco; ≥3 GK por clube; XI com exatamente 1 GK | V13 |
| 10 | `crest.sourceCitation` com URL verificável nos 20 | §3.1 |
| 11 | `EpithetLift` apenas onde o nível da alcunha é ≤ 1 | D-45 |

Recálculo completo: **3.488 fórmulas, 0 erros.**

---

## 6. Aderência ao dado real (pós-recalibração D-46)

| Grandeza | Lote | Real | Fonte |
| :--- | ---: | ---: | :--- |
| Capacidade média | 37.900 | 43.931 | Estádios da Série A 2026 |
| Desvio de capacidade | 14.289 | 19.159 | idem |
| Faixa de capacidade | 12.500–62.000 | 12.000–78.838 | idem |
| Jogador mais caro | € 24,1 mi | € 35 mi | Transfermarkt 28/01/2026 |
| Elenco mais caro | € 260,4 mi | € 209,9 mi | idem |
| **Elenco mediano** | **€ 91,6 mi** | **€ 92,3 mi** | idem |
| Elenco mais barato | € 13,0 mi | € 14,6 mi | idem |
| **Salário médio** | **R$ 261.458/mês** | **R$ 253.573/mês** | CBF/ANRESF 1/2026 |
| Maior salário | R$ 2,08 mi/mês | ~R$ 3,0 mi/mês | Flashscore 2025 |

A aba `Fontes` do arquivo lista cada número com URL. Elenco mediano (−0,7%) e salário médio (+3,1%) são os melhores resíduos já obtidos — a correção do goleiro melhorou os dois, porque 73 jogadores deixaram de estar subavaliados. O resíduo de jogador mais caro e de elenco topo é a tensão de **D-43 §6.1**: é forma da curva, não escala.

**Ordem importa.** O refit econômico foi feito depois da correção do goleiro. Com o bug de pé, o jogador mais caro do lote era um goleiro artefato de OVR 95 valendo € 48,7 mi; ajustar as constantes contra ele teria lavado o defeito para dentro da calibração.

---

## 7. O que continua sem resolver

Consolidado em **§9 do Registro de Decisões D-38…D-49**. Sobram dois: o limiar de ΔE (bloqueado pelo renderizador, D-04) e as **tabelas de `CapProfile` por posição** — agora o único item de fundo em aberto, e com acoplamento declarado: 5,5% dos valores nos slots de maior peso estão em 90+, exatamente onde o cap morde, então aterrissar `CapProfile` obriga a re-medir o teto de OVR e refitar `valuePivot` no mesmo passo (§13.5).

Fechados na rodada 3: o débito de OVR (teto 88), o bug do arquétipo de goleiro, o vetor de OVR do goleiro, e a perna "tratamento de goleiro dentro dos 12" do item de partição.

---

## 8. O que isto destrava

O lote é **elegível a produção**: 20 de 20 clubes com fatos verificados e citados, e nenhum número inventado sem etiqueta. O `CrestGenerator` e o `KitAssembler` têm 20 entradas reais. O harness de calibração tem 688 jogadores com distribuição conhecida, teto de OVR dentro do alvo (88, contra 79 no original) e nenhuma posição isolada no topo.

**A prioridade não muda.** Autoria continua atrás de **D-07**. Duas coisas já entraram no `SimCore.cs` real e não são mais exceção pendente: o patch de arredondamento (**D-40**) e o vetor de OVR do goleiro (**D-49**), este último com um aviso no laço de papel cruzado para quem for portá-lo de novo.
