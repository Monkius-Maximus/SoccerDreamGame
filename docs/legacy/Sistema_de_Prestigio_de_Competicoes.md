# Pesquisa: Sistema de "Prestígio de Competições" — Terra Paralela

## Sumário executivo

Recomendação central: use uma **combinação** de duas fontes públicas como espinha dorsal do ranking de prestígio — **Opta Power Rankings (agregado por liga)** como base primária de força esportiva atual e cobertura global, e o **coeficiente de país da UEFA** (+ equivalentes AFC/CONMEBOL/CAF/CONCACAF) como camada de validação e para a Europa. Proxies econômicos do Transfermarkt (valor de mercado agregado por liga) entram como terceira camada de desempate. Nenhuma fonte isolada cobre o mundo inteiro com granularidade suficiente; a força do sistema está na triangulação.

Achado que altera o design (Eixo C + Eixo B): a premissa "quanto mais difícil a competição, maior o ganho de desenvolvimento por partida" **não é sustentada de forma limpa** nem pela literatura esportiva nem pelos precedentes de jogos. A literatura mostra que o desenvolvimento é impulsionado por **minutos de jogo competitivo em nível apropriado** (nem fácil demais, nem difícil demais), não por dificuldade máxima. E a engenharia reversa da comunidade de Football Manager (FM-Arena, thread 13975) indica de forma explícita que a **reputação da liga, isoladamente, não teve efeito mensurável** sobre o crescimento de atributos em testes controlados — verbatim: *"league reputation: 50,100,150,200 reputation makes no difference, the conclusion is that league reputation does not affect growth."* O que impulsiona o crescimento são minutos, ratings de partida, o gap entre potencial e habilidade atual, idade e personalidade. Isso recomenda modelar o ganho de desenvolvimento como função de **adequação (fit) entre o nível do jogador e o tier da liga**, e não como um multiplicador monotônico "tier alto = ganho alto".

Estrutura piramidal (Eixo D): o mundo tem 211 associações-membro da FIFA; uma banda de 5–6 faixas de prestígio é defensável e alinhada com os cortes naturais que aparecem consistentemente entre os métodos (elite europeia; segundo escalão europeu + Brasil; ligas nacionais fortes fora da elite; ligas médias; ligas periféricas; semi-pro/amador). As fronteiras controversas são bem localizadas: segundas divisões europeias fortes (Championship, Serie B) vs. primeiras divisões nacionais menores; e ligas de petrodólar/mercados emergentes (Saudi Pro League, ex-Chinese Super League) onde valor de mercado e força esportiva divergem fortemente.

---

## EIXO A — Como o mundo real mede força/prestígio de ligas

### A.1 Coeficientes UEFA (país e clube) — [Documentado]

**Fórmula do coeficiente de país (association coefficient).** Cada temporada, a UEFA soma todos os pontos obtidos pelos clubes de uma associação na Champions League, Europa League e Conference League, e divide pelo número de clubes daquela associação que participaram das três competições naquela temporada. O coeficiente de país é a soma dos coeficientes de 5 temporadas consecutivas (janela móvel de 5 anos; a temporada mais antiga cai a cada verão). Pontuação base: 2 pontos por vitória, 1 por empate (metade nas fases de qualificação). Bônus por avançar às fases de mata-mata: +1,5 por rodada na Champions, +1 na Europa, +0,5 na Conference. Coeficientes calculados até o milésimo, sem arredondamento para cima. (Fonte primária: UEFA.com, "How men's association club coefficients are calculated".)

**Coeficiente de clube.** Desde 2018, é a soma dos pontos do próprio clube nas últimas 5 temporadas europeias, **ou 20% do coeficiente da sua associação no mesmo período, o que for maior**. Antes de 2018, a contribuição do coeficiente de país era maior (33% em 2004–2008; 50% antes de 2004). Existe também um ranking de clube de 10 anos usado desde 2024/25 para distribuir parte da receita da Champions (desde 2024 sem os pontos históricos de título). (Fontes: Wikipedia "UEFA coefficient"; kassiesa.net.)

**Janela temporal:** 5 anos (móvel) para seeding e vagas; 10 anos para distribuição de receita.

**Críticas conhecidas [Documentado/Prática de comunidade]:** (1) mede desempenho **continental**, não a força doméstica direta — uma liga com 2–3 clubes de elite e uma base fraca pode pontuar alto (Portugal é o exemplo clássico: rankeado bem acima do seu tamanho econômico, sustentado por Benfica, Porto e Sporting); (2) inércia — a janela de 5 anos recompensa consistência e reage devagar; (3) cobre **apenas a UEFA** — inútil como métrica direta para 5 das 6 confederações; (4) o coeficiente de clube com piso de 20% do país "empresta" prestígio do país ao clube, distorcendo clubes individuais.

### A.2 Equivalentes de outras confederações — [Documentado / Incerto]

- **AFC (Ásia): AFC Club Competitions Ranking** (antes "MA ranking"), lançado em 2014. Desde 2024–25, usa desempenho dos clubes nas **últimas 8 temporadas** com fórmula ponderada (mais peso a temporadas recentes); pontos por país = média do desempenho de todos os clubes do país nas competições de clubes da AFC (Champions League Elite, Champions League Two, Challenge League) da fase de grupos em diante. A associação nº1 recebe 100 pontos; as demais, proporcionalmente menos. Cobre as 47 associações-membro. Saudi Arabia liderava com 113,195 pontos em novembro de 2025. [Documentado]
- **CONMEBOL (América do Sul): Ranking de Clubes da CONMEBOL**, anual desde 2015 (primeira aplicação: edição 2016 da Libertadores). Três critérios: desempenho na Libertadores/Sudamericana nos últimos 10 anos; coeficiente histórico (Libertadores desde 1960, Sudamericana desde 2002); e bônus para campeões nacionais dos últimos 10 anos. Usado para seeding/potes da Libertadores e Sudamericana. [Documentado]
- **CAF (África) e CONCACAF (América do Norte/Central):** rankings de coeficiente de clubes/associações existem para seeding das respectivas competições continentais (confirmado como categoria por múltiplas fontes secundárias; a metodologia detalhada exata **não foi recuperada em fonte primária** nesta pesquisa — classificar como [Incerto] quanto aos detalhes da fórmula; confirmar antes de usar como camada de validação para esses continentes).

**Característica comum a todos os coeficientes confederacionais:** medem desempenho **continental de clubes**, não força doméstica intrínseca, e são **intra-confederação** (não comparáveis entre confederações sem uma ponte externa). Servem melhor como ranking *dentro* de uma banda continental do que como escala global.

### A.3 Sistemas Elo (ClubElo e similares) — [Documentado/Prática de comunidade]

- **ClubElo (clubelo.com):** aplica o sistema Elo (adaptado com vantagem de casa e diferença de gols) a clubes; atualização contínua (semanal/por partida); dados históricos e diários acessíveis publicamente e via bibliotecas (soccerdata, penaltyblog). K-factor típico ~20. **Limitação de cobertura crítica:** é **limitado a clubes europeus** (inclui países menores e divisões inferiores da Europa, mas não cobre outras confederações). Isso o torna inadequado como base *global* única.
- **World Football Elo / FootballDatabase / worldclubratings.com:** adaptações do Elo (ou do método Elo da FIFA) para cobertura global (7.000+ times, 200+ países em uma das fontes). worldclubratings.com usa um fator de importância (I) por competição — e explicitamente atribui **menos peso às Champions Leagues de confederações "não tão competitivas"**, um julgamento editorial embutido. Metodologia mais opaca e menos citada academicamente que ClubElo.
- **Nota metodológica útil para o projeto:** o sistema Elo do World Football Elo Ratings, ao promover um time à primeira divisão, atribui um rating inicial **dependente da força da liga** (ex.: Inglaterra 1500, França 1450, Holanda 1400, Grécia 1350, até ~1250 para ligas fracas). Esse é um precedente direto e público de "âncora de rating por tier de liga" — exatamente o padrão que o gerador de Terra Paralela usa com `leagueTier`.

### A.4 Rankings baseados em dados — [Documentado]

- **Opta Power Rankings (Stats Perform / The Analyst):** sistema global que, segundo a própria The Analyst (Opta), *"assess and rate more than 15,000 men's and women's domestic teams across the globe on advanced performance metrics. Each team receives a score on a global scale, from 0 (the weakest) to 100 (the strongest)."* Algoritmo derivado de Elo com ajustes de diferença de gols e qualidade da competição, atualizado a cada partida. Cobre **250+ ligas**. Crucialmente, a Opta publica um **ranking de força de liga** tirando a média dos ratings dos clubes de cada competição, com três visões: média de todos os clubes, média do top-10 e média do top-5 — o que permite distinguir ligas "rasas com poucos gigantes" (Portugal, Holanda) de ligas "profundas" (Championship inglesa). Valores publicados pela The Analyst / Opta Analyst em "Strongest Leagues in the World" (junho 2025): Premier League — *"The average team rating of the 20 Premier League clubs that will play in the 2025-26 competition on a scale of zero to 100... is 92.6."* Serie A e La Liga empatadas em 87,0; Bundesliga 86,3; Ligue 1 85,5 (5º); Brasileirão 79,4 (9º, maior liga não-europeia); Eredivisie 78,8; Primeira Liga portuguesa 79,8; Argentina Primera 78,6 (11º); MLS 78,5 (12º, empatada com a Liga MX); Championship inglesa rankeia 6º geral quando todos os clubes contam; Serie B italiana é a 2ª divisão mais forte (18º); Saudi Pro League 29ª. **A visão pública (The Analyst / dataviz) é gratuita**; o acesso completo à API é comercial.
- **FiveThirtyEight Global Club Soccer Rankings / SPI (Soccer Power Index):** ratings SPI com componentes ofensivo/defensivo, cobrindo ~40 ligas de clubes; dados históricos (match-by-match desde 2016) foram **publicados abertamente em CSV no GitHub** (soccer-api / fivethirtyeight/data). **Limitação:** cobria clubes de ligas de topo e algumas fora da Europa, mas **não** ligas europeias de divisões inferiores nem países pequenos; e o FiveThirtyEight foi descontinuado (operação encerrada pela ABC News), então os dados existem como **snapshot histórico, não mais atualizado** — utilizável como referência agregada estática, não como feed vivo. [Documentado quanto aos dados; status de descontinuação classificado como amplamente reportado.]
- **IFFHS "Strongest National League in the World":** ranking anual desde 1991. Metodologia: considera **apenas os 5 melhores clubes de cada país** (não a liga inteira), somando pontos em competições nacionais (liga + copa nacional a partir das oitavas) e continentais/intercontinentais. Sistema de 4 níveis por país (clubes de ligas nível 1 ganham 4 pts por vitória, 2 por empate; nível 2 ganha 3/1,5; e assim por diante). Cobre **60+ ligas** (usado para prêmios como artilheiro mundial). Publicado gratuitamente. **Críticas:** só olha o topo (ignora profundidade); a IFFHS não tem afiliação com a FIFA; a comunidade repetidamente pergunta pela metodologia exata nos comentários oficiais. La Liga dominou historicamente (12+ títulos). **Evolução recente (importante datar corretamente):** no ranking IFFHS de **2024**, a Serie A italiana ainda figurava como a nº1; no ranking de **2025** (divulgado em jan/2026), a **Premier League** foi eleita a melhor com **2.369 pontos**, à frente de La Liga e do Brasileirão (com a Serie A italiana caindo ao 4º e a Bundesliga em 5º, com 1.880 pontos). Na métrica IFFHS especificamente, a **Saudi Pro League** subiu vertiginosamente: 40ª em 2022 (352,75 pts), 33ª em 2023 (479,5 pts), 21ª em 2024 (563,75 pts) e **13ª em 2025 (868,75 pts)** — trajetória bem diferente da sua posição na Opta (29ª), ilustrando a divergência entre métodos (ver D.3).

### A.5 Proxies econômicos (Transfermarkt) — [Documentado/Prática de comunidade]

Transfermarkt publica **valor de mercado agregado por liga e por clube**, com datas de corte selecionáveis. É a proxy econômica mais granular e pública. **Correlação com força esportiva:** forte no núcleo europeu, mas com viés documentado — uma análise da Berkeley Sports Analytics Group encontrou que os valores do Transfermarkt exibem **viés por liga**, com Premier League e Ligue 1 tendendo a valores médios mais altos mesmo controlando por desempenho, correlacionados com o coeficiente UEFA de país. **Divergência crítica para o design:** proxies econômicas superavaliam ligas de petrodólar/mercados inflados (Saudi Pro League, ex-Chinese Super League) relativamente à sua força esportiva medida por Elo/Opta — exatamente onde os métodos discordam. Os valores são estimativas colaborativas (crowd-sourced), não registros de transação; usar como **camada agregada de desempate**, nunca como verdade individual.

---

## EIXO B — Como jogos modelam prestígio/reputação de competição

### B.1 Football Manager — [Documentado + Prática de comunidade]

**Escala interna de reputação.** FM usa reputação numérica: para **ligas/competições**, escala interna de **1–200** (o editor pré-jogo permite inserir qualquer inteiro de 1–200, sincronizado a um sistema de estrelas; 200 = mínimo para 5 estrelas; 199 ≈ 4,5 estrelas; faixas low/mid/high dentro de cada nível de estrela para diferenciar ligas de mesma reputação). Para **clubes e pessoas (jogadores/staff)**, a reputação usa escala **0–10000** (ex.: League Two ~2000–3000; melhores clubes do mundo ~9000–9500; jogador atinge status continental >5250 e mundial >7500 em versões antigas). Atributos de habilidade (CA/PA) e "reputation" de staff usam 0–200. (Fontes: FMDataLab; Football Manager Wiki; RCM staff_attr.)

**Como a reputação de liga afeta o jogo [Prática de comunidade]:**
- **Transferências e valor de mercado:** o valor de mercado calculado pelo FM é **mantido baixo pela baixa reputação da liga** em que o jogador atua; jogadores similares em Serie A/La Liga/Premier/Bundesliga valem 4–5× mais (Strikerless, estudo de caso J-League/Brasil).
- **Disposição a transferir / felicidade:** o melhor jogador de uma liga fraca fica descontente mais rápido e quer sair; manter esses jogadores é mais difícil e, se conseguir, aumenta a reputação do clube.
- **Intake de juvenis:** a qualidade do youth intake depende de reputação do clube, youth recruitment, facilities, junior coaching, Head of Youth Development e do **"youth rating" da nação** — não diretamente da reputação da liga.

**Desenvolvimento de jogador vs. nível de competição — o achado que altera o design [Prática de comunidade / engenharia reversa]:**
- Testes controlados da comunidade (**FM-Arena, thread 13975**, "How training facilities, match played, height, league reputation... affect attribute growth") concluíram que **a reputação da liga, isoladamente, NÃO afeta o crescimento de atributos**, verbatim: *"league reputation: 50,100,150,200 reputation makes no difference, the conclusion is that league reputation does not affect growth."* Os drivers mensuráveis do ganho de CA são:
  - **Número de partidas jogadas** — verbatim (mesma thread): *"97.4% participated in 100 games in 30 games per season, 88.4% in 20 games... About 25 games is enough for 95% grow."* Ou seja, ~25 jogos/temporada já capturam ~95% do ganho possível.
  - **Bons ratings de partida.**
  - O **gap entre PA e CA** (crescimento forte quando o gap ≥ 20 pontos).
  - **Idade** (<28; aos ~18 é ~50/50 treino vs. jogo, aos ~23 a experiência de jogo pesa muito mais).
  - **Personalidade**, quantificada pela FM-Arena: *"20 Professionalism get approach twice as much growth per season as 10 Professionalism"* e, para Ambition, *"10 for the standard 100% effect, Ambition 1 has 73.1% effect, Ambition 20 has 104.9% effect."*
- Existe debate **não resolvido** na comunidade sobre se a reputação da **equipe adversária** (não da liga) impõe um teto ao CA — relatos anedóticos de jogadores "travando" em ~120 CA em ligas de meia-estrela e subindo a ~140 quando os adversários melhoraram —, mas **nenhum teste confirmou** isso.
- A orientação **oficial da Sports Interactive** e da comunidade convergem no conceito de "jogar em **nível apropriado ou ligeiramente acima**": a SI (footballmanager.com, FM26) diz que aos 18 o game time importa e que a chave é achar clubes/divisões onde o jovem tenha **quality game time e jogue bem** (minutos E rating importam); a Passion4FM afirma que jogar contra oponentes muito abaixo do nível **quase não ensina nada**, e que ratings baixos prejudicam confiança/moral e travam o desenvolvimento.

**Síntese FM:** o mecanismo real não é "liga difícil → mais desenvolvimento", e sim "**minutos + bons ratings em nível adequado ao jogador**". Dificuldade excessiva (jogador fraco em liga forte) reduz ratings e trava o desenvolvimento; dificuldade insuficiente (jogador forte em liga fraca) também não ensina. Isso é uma curva de **fit**, não uma rampa monotônica.

### B.2 Football Superstar 2 (Lazy Boy Developments) — [Documentado marketing + Prática de comunidade]

- Progressão de carreira é **gated por reputação**: conforme a reputação sobe, clubes/ligas maiores tentam contratar o jogador (descrição oficial: *"as your reputation improves, bigger and better clubs will attempt to sign you"*). O core loop é ganhar experiência → **upgrades pagos de atributos** com moeda do jogo (consenso da comunidade: pay-to-progress; reviews afirmam ser "impossível" chegar a 99 overall sem gastar).
- **Não há documentação (oficial ou de comunidade) de que uma liga mais difícil multiplique os ganhos de stat/rating por partida.** A reputação **abre acesso** a ligas/clubes melhores e **escala recompensas/salários**, mas os alavancadores de performance por partida documentados nos patch notes são o "1v1 bonus" e o "effort" em jogo, não a dificuldade da liga. Reviews descrevem frustração por ficar preso em ligas pequenas e ver a IA "evoluir duas vezes mais rápido" (verbatim de review na App Store: *"it feels like all the 'AI' players around me level up twice as fast as I am"*).
- **Implicação para Terra Paralela:** FS2 é precedente de **reputação-como-gate-e-recompensa**, não de **dificuldade-como-multiplicador-de-desenvolvimento**.

### B.3 Outros precedentes — [Prática de comunidade]

- **New Star Manager / New Star Soccer:** estrutura piramidal explícita por país (ex.: Alemanha começa na 3ª divisão; Espanha teve 3ª divisão em 4 grupos regionais B1–B4 até 2020); promoção/rebaixamento **um tier por vez**; ao vencer a divisão máxima, o jogador pode trocar de país. Países de **liga única** (Austrália, Arábia Saudita, Irã) são atalho para vencer no primeiro ano. Progressão do elenco é por Star Points/cards e recrutamento juvenil com cooldown (ex.: cooldown inicial de 8 partidas para recrutar novo juvenil) — **sem** vínculo documentado entre dificuldade da liga e velocidade de desenvolvimento de atributos.
- **EA Sports FC / FIFA Career Mode:** desenvolvimento dirigido por **game time + performance (match rating) + moral + treino + coaches**, com **potencial dinâmico** (um jovem 70 com boas atuações salta para meados dos 80). Comunidade (RPGStash, Sportskeeda) reporta que **a liga em si tem efeito mínimo na velocidade de desenvolvimento** — jogadores em ligas inferiores podem até crescer mais rápido por terem ratings/forma melhores. Ressalva importante: empréstimos só rendem "match XP realista" se forem para ligas com **simulação profunda** (deep simulation) selecionada, e o conselho é emprestar para onde o jovem **jogará como titular** — ou seja, o que importa é minutos e nível de simulação, não prestígio.
- **Top Eleven / OSM:** não recuperados em fonte confiável nesta pesquisa — classificar como [Incerto].

**Conclusão transversal do Eixo B:** nenhum dos precedentes de maior fidelidade (FM, EA FC) valida "competição mais difícil = mais desenvolvimento por partida". Todos convergem em **minutos + performance em nível adequado**. Isso reforça a recomendação de C.

---

## EIXO C — Desenvolvimento de jogador vs. nível de competição

### C.1 Empréstimos para divisões inferiores — [Documentado]

- Estudo qualitativo (academias inglesas, sub-23, 9 participantes, análise temática) concluiu que empréstimos para **clubes de divisões inferiores** contribuem para o desenvolvimento **físico e psicossocial** do jogador — a limitação do sub-23 é a falta de intensidade competitiva "real", e o empréstimo para baixo supre isso. Ou seja, **descer de nível** pode acelerar desenvolvimento quando fornece minutos competitivos que o nível de origem não dá.
- Revisão sobre transição juvenil→sênior na Europa (31 ligas de primeira divisão, 2009–2020, PMC8758176) documenta que empréstimos de curto prazo para clubes de nível inferior e times B em ligas inferiores foram **desenhados justamente para desenvolver e promover jovens** — a lógica institucional é dar tempo de jogo competitivo, não expor a jovens à maior dificuldade possível.

### C.2 Minutos e nível apropriado — [Documentado]

- A literatura converge: **tempo de jogo (minutos) em ambiente competitivo** é o preditor central. Um estudo (sub-16 elite, temporada completa, PMC9115988) mostrou que VO₂max, maturação e potência explicam 76% dos minutos jogados — capturando o entrelaçamento entre maturação física, seleção e oportunidade de jogo.
- O conceito prático (corroborado pela literatura e pela orientação de desenvolvimento) é o de **desafio ótimo**: jogar no nível de operação atual "ou ligeiramente acima". Nível fácil demais ensina pouco; difícil demais gera baixas atuações, que prejudicam confiança e travam a curva.

### C.3 Idade-pico por posição — [Documentado]

Consenso de múltiplos estudos revisados (Dendir 2016; estudo sueco Bayesiano hierárquico; Frontiers/Champions League):
- **Pico geral: 25–27 anos.**
- **Atacantes/forwards: ~25** (pico mais cedo, declínio pós-pico mais íngreme).
- **Meio-campistas: 25–27.**
- **Zagueiros/defensores: ~27** (curvatura mínima, declínio lento e prolongado).
- **Goleiros: por volta de 27+** (declínio longo e lento; pico mais tardio).
- Há tendência secular de "envelhecimento" do pico (investimento em preparação física estende a longevidade), e GK/zagueiros centrais tendem a picar mais tarde que atacantes.

### C.4 Efeito de idade relativa (RAE) — [Documentado]

O RAE (viés para nascidos no início do ano de seleção) é robusto e **amplificado onde a competição por vagas aumenta** e em cut-offs de seleção. Relevante para o gerador demográfico de Terra Paralela: ligas/tiers de elite terão viés de seleção mais forte, e há literatura sobre **reversão do RAE** em níveis de elite (os mais jovens selecionados que "sobrevivem" tendem a ter skill técnico superior). Isso é insumo para distribuições demográficas por tier, não para a mecânica de desenvolvimento em si.

### C.5 Veredito do Eixo C sobre a mecânica proposta

A premissa "quanto mais difícil a competição, maior o ganho de desenvolvimento por partida" é **parcialmente refutada** na forma monotônica. O que a evidência sustenta:
1. **Minutos competitivos** são o driver primário — mais do que o prestígio do palco.
2. Existe um **nível ótimo de desafio** (fit), não "quanto mais difícil melhor". Descer de nível (empréstimo) frequentemente acelera o desenvolvimento por garantir minutos.
3. **Idade e posição** modulam a janela de ganho (jovens ganham muito; ganho cai forte após o pico específico da posição).
4. Performance (rating) medeia o efeito: jogar bem no nível atual desenvolve; jogar mal num nível alto demais não.

Portanto, a mecânica correta não é `ganho = f(tier)` crescente, e sim `ganho = f(minutos, fit_jogador_vs_tier, idade, posição, performance)`, com o `leagueTier` entrando como o parâmetro que define o **alvo de dificuldade** contra o qual o fit é medido.

---

## EIXO D — Estrutura piramidal mundial

### D.1 Escala do universo — [Documentado]

- **211 associações-membro da FIFA**, cada uma com ao menos uma liga doméstica; estimativas passam de **10.000 ligas** globalmente contando todos os níveis pro/semi-pro/amador.
- Pirâmides nacionais variam enormemente: a Inglaterra tem um sistema com muitos níveis interligados por promoção/rebaixamento (mais de 7.000 times em ~5.300 clubes); Alemanha e Romênia têm 3 níveis nacionais profissionais e depois regionalização; muitos países têm **liga única** (Austrália, Arábia Saudita, Irã). As três primeiras divisões costumam ser o "núcleo relevante" para simulação em quase todos os países.
- Para Terra Paralela, o "mundo relevante" prático são as **1ª–3ª divisões das ~50–70 associações com estrutura profissional documentada**, mais as 1ª divisões das demais — algo na ordem de 150–300 competições, não 10.000.

### D.2 Proposta de bandas de prestígio (5–6 faixas) — [síntese, cortes sustentados pelos rankings do Eixo A]

Cortes onde **múltiplos métodos concordam** (candidatos a fronteiras "duras"):
- **Banda 1 — Elite global:** Premier League, La Liga, Bundesliga, Serie A, Ligue 1. (Opta 85,5–92,6; topo do coeficiente UEFA; IFFHS.) A Premier League destaca-se em classe própria por profundidade (Opta 92,6 vs. ~87 das demais).
- **Banda 2 — Primeiro escalão forte / segundo europeu de elite:** Primeira Liga (POR ~79,8), Eredivisie (~78,8), Brasileirão (~79,4), Liga Profesional Argentina (~78,6), Championship inglesa (6ª geral por profundidade), Belgian Pro League. Aqui reside a **fronteira mais controversa**: por profundidade a Championship supera várias 1ª divisões; por prestígio/coeficiente ela é uma 2ª divisão.
- **Banda 3 — Ligas nacionais médias/fortes fora do topo:** Liga MX e MLS (~78,5), Süper Lig turca, primeiras divisões escandinavas/da Europa central fortes, Serie B italiana (~2ª divisão mais forte do mundo, Opta 18º geral).
- **Banda 4 — Ligas médias / periféricas competitivas:** Saudi Pro League (Opta 29ª — **fronteira controversa** por divergência valor de mercado × força esportiva; ver D.3), K League, J1 League, ligas do leste europeu, África do Norte (Tunísia, Egito lideram a CAF).
- **Banda 5 — Ligas nacionais fracas / emergentes:** primeiras divisões do sudeste asiático (Tailândia, Vietnã, Malásia, Indonésia), maioria de CAF subsaariana, Oceania (Tahiti, Nova Zelândia lideram a OFC mas em ~111º+ mundial na IFFHS).
- **Banda 6 (opcional) — Semi-pro/amador e divisões inferiores nacionais.**

### D.3 Fronteiras controversas (divergência entre métodos) — [Documentado]

1. **Segundas divisões europeias fortes vs. primeiras divisões nacionais menores:** Championship/Serie B batem muitas 1ª divisões nacionais por profundidade Opta, mas coeficiente e IFFHS (que só olham topo/continental) as ignoram como 2ª divisão.
2. **Ligas de petrodólar / mercados inflados:** a Saudi Pro League é o caso extremo — **valor de mercado agregado alto** e coeficiente AFC alto (Saudi lidera a AFC com 113,195 pts em nov/2025), e escalada meteórica na IFFHS (da 40ª em 2022 à 13ª em 2025), mas a Opta a coloca apenas na **29ª** posição por profundidade esportiva. A antiga Chinese Super League seguiu padrão semelhante (alto gasto, baixa profundidade). Máxima divergência entre proxy econômica/continental e força medida por dados.
3. **Cobertura desigual:** ClubElo não cobre nada fora da Europa; FiveThirtyEight (estático) e Opta cobrem globalmente mas com granularidade decrescente nas ligas menores; IFFHS cobre 60+ mas só via top-5 clubes. Ligas de baixa cobertura (PSL sul-africana, sudeste asiático) terão maior incerteza de banda.

---

## Tabela consolidada — comparação dos métodos de ranking

| Método | Cobertura global | Granularidade | Atualização | Disponibilidade pública | Adequação como base do nosso tier |
|---|---|---|---|---|---|
| **Coeficiente UEFA (país)** | Só UEFA (55 assoc.) | Por associação (não por clube na versão país) | Contínua / anual (janela 5 anos) | Total (UEFA.com) | Alta **para a Europa**; nula fora dela. Mede força continental, não doméstica. |
| **Coeficientes AFC/CONMEBOL/CAF/CONCACAF** | Cada uma só a sua confederação | Por associação | Anual/contínua (janelas 4–10 anos) | Pública (CAF/CONCACAF com detalhes [Incerto]) | Boa para ordenar **dentro** de cada continente; não comparável entre confederações. |
| **ClubElo** | Só Europa (inclui divisões inferiores) | Por clube, diária | Contínua (por partida) | Total (site + libs) | Excelente para a Europa; inútil como base global única. |
| **Opta Power Rankings** | Global, 250+ ligas, 15.000+ times | Por clube E média por liga (top-5/10/todos) | Por partida | Visão pública gratuita; API comercial | **Melhor base primária global**: cobertura + granularidade + visão explícita de força de liga. |
| **FiveThirtyEight SPI** | ~40 ligas (parcial fora da Europa) | Por clube (off/def/SPI) | **Descontinuado** (snapshot histórico) | Total (CSV GitHub) | Referência agregada estática; não usar como feed vivo. |
| **IFFHS Strongest League** | 60+ ligas, todos os continentes | Por liga (via top-5 clubes) | Anual | Total (iffhs.com) | Boa camada de validação cross-continental; ignora profundidade. |
| **Transfermarkt (valor por liga)** | Ampla (onde há mercado) | Por liga e por clube | Contínua | Pública (site) | Proxy econômica de desempate; viés por liga; superavalia mercados inflados. |

---

## Decisões prontas para o design doc

### 1. Método base do ranking de prestígio (recomendação fundamentada)

**Usar uma combinação em três camadas:**
- **Camada primária — Opta Power Rankings (média por liga).** É o único método com **cobertura global (250+ ligas), granularidade por clube e uma métrica de força de liga já pronta e pública**. Usar as três visões da Opta (média de todos os clubes, top-10, top-5) para capturar profundidade vs. concentração.
- **Camada de validação continental — coeficientes das confederações** (UEFA de país para a Europa; AFC/CONMEBOL/CAF/CONCACAF para os demais). Servem para ordenar ligas *dentro* de cada continente e para checar a plausibilidade do ranking Opta, especialmente onde a cobertura Opta é rala.
- **Camada de desempate econômico — Transfermarkt (valor agregado por liga)**, usada **apenas** quando as duas camadas anteriores empatam ou divergem, e com a ressalva explícita de superavaliação de mercados inflados (Saudi Pro League é o alerta padrão).

Justificativa: nenhuma fonte isolada é global + granular + viva. Opta resolve cobertura e profundidade; coeficientes resolvem a comparação intra-continental confiável; Transfermarkt resolve empates. FiveThirtyEight/IFFHS entram como validação estática adicional, não como base.

**Regra de rigor (metodologia "research before thresholds"):** todo valor de banda deve rastrear a um número publicado (score Opta de liga, coeficiente, ou valor Transfermarkt). Nunca usar registros individuais de jogadores de bases proprietárias — apenas agregados por liga.

### 2. Mapeamento ranking → bandas de prestígio → faixa de `leagueTier` (estrutura, sem fixar floats)

Estrutura recomendada: **6 bandas** mapeadas para a faixa existente `leagueTier ∈ [~0.5, 1.0]`, ancoradas nos cortes onde os métodos concordam (Eixo D.2). Os clubes de elite (âncoras curadas manualmente) definem o **teto** de cada banda superior; as bandas inferiores são geradas a partir do tier + demografia.

| Banda | Descrição | Sinal quantitativo de corte (dado que sustenta) | Posição relativa em `leagueTier` |
|---|---|---|---|
| B1 | Elite global | Opta média liga ≳ 85; topo coeficiente UEFA | Teto (≈1.0), com a Premier League isolada no topo (Opta 92,6 vs ~87) |
| B2 | Escalão forte / 2º europeu de elite | Opta ~78–80; alto coeficiente com poucos clubes | Alto |
| B3 | Ligas nacionais médias-fortes | Opta ~76–79; líderes fora do eixo europeu | Médio-alto |
| B4 | Médias/periféricas competitivas | Opta ~70–76; líderes de AFC/CAF; ligas infladas | Médio |
| B5 | Nacionais fracas/emergentes | Opta < ~70; baixa cobertura; sudeste asiático, OFC | Baixo |
| B6 | Semi-pro/amador e divisões inferiores | Fora dos rankings globais | Piso (≈0.5) |

**Importante (não fixar floats agora):** os cortes numéricos de score Opta acima são **indicativos dos pontos onde os métodos concordam**, não thresholds finais — os floats de `leagueTier` por banda são **decisão de design** (ver seção 4), a ser calibrada contra as âncoras curadas de ligas de elite já existentes no gerador. A estrutura (6 bandas, teto ancorado nos clubes de elite, piso em semi-pro) é o que a pesquisa sustenta.

### 3. Como modular ganho de desenvolvimento por banda (com base no Eixo C)

**Não implementar** um multiplicador monotônico "tier alto → ganho alto por partida" — a pesquisa (Eixo C + engenharia reversa FM no Eixo B) **não sustenta** isso e sugere que travaria jovens em ligas fortes e desperdiçaria o valor de empréstimos para baixo.

**Implementar em vez disso um modelo de "fit" (desafio ótimo):**
- Definir o ganho de desenvolvimento por partida como função de **(a) minutos jogados**, **(b) fit entre a habilidade atual do jogador e o `leagueTier` da competição**, **(c) idade/posição** e **(d) performance na partida** — espelhando os drivers documentados (minutos: ~25 jogos/temporada capturam ~95% do ganho no FM; rating; gap PA–CA ≥ 20; idade < 28; personalidade).
- **Curva de fit:** ganho máximo quando o jogador atua num tier **igual ou ligeiramente acima** do seu nível atual; ganho reduzido quando o tier é **fácil demais** (aprende pouco) e ganho reduzido/negativo para confiança quando é **difícil demais** (atuações ruins). Isso permite que empréstimos para tiers inferiores **acelerem** o desenvolvimento de um jovem preso num elenco de elite — comportamento validado pela literatura de empréstimos (Eixo C.1).
- **Janela por idade/posição:** ganho decai fortemente após a idade-pico específica da posição (atacantes ~25, meias 25–27, zagueiros ~27, goleiros 27+). Usar isso para modular a magnitude do ganho, não o `leagueTier`.
- **Papel do `leagueTier` no desenvolvimento:** ele define o **alvo de dificuldade** contra o qual o fit é medido (e o teto assintótico de habilidade que a exposição àquele nível pode ajudar a atingir), **não** um multiplicador direto de XP. Precedente público direto: o World Football Elo atribui rating inicial por força de liga — o mesmo princípio de "âncora por tier".

### 4. Lacunas que permanecem decisão de design (não de pesquisa)

Estas **não** devem ser resolvidas por mais pesquisa; são escolhas de tuning/produto:
1. **Os floats finais de `leagueTier` por banda** e a largura de cada banda (a pesquisa dá a estrutura e os pontos de corte; os valores exatos são calibração contra as âncoras curadas).
2. **Número final de bandas (5 vs 6)** — se colapsar semi-pro/amador na B5 ou mantê-la separada.
3. **Forma exata da curva de fit** (quão íngreme é a penalidade por "difícil demais"; se há ganho negativo real ou apenas ganho zero + penalidade de moral).
4. **Peso relativo das três camadas do ranking** (Opta vs. coeficiente vs. Transfermarkt) e como resolver divergências nas fronteiras controversas (Championship, Saudi Pro League).
5. **Tratamento de divisões inferiores dentro de um mesmo país** (como derivar o tier da 2ª/3ª divisão a partir do tier da 1ª — offset fixo? proporção?).
6. **Frequência de recálculo do tier** no mundo simulado (estático por save vs. dinâmico como a reputação do FM, que se move ~1 ponto/temporada).
7. **Como mapear confederações de baixa cobertura** (PSL sul-africana, sudeste asiático) quando Opta é ralo e só há coeficiente continental — quanto peso dar a cada sinal.

---

## Caveats

- **Descontinuação do FiveThirtyEight:** os dados SPI existem como snapshot histórico aberto, mas não são mais atualizados; tratá-los como referência estática.
- **Detalhes de fórmula de CAF/CONCACAF** não foram recuperados em fonte primária nesta pesquisa — classificados como [Incerto]; confirmar antes de usar como camada de validação para esses continentes.
- **Engenharia reversa vs. documentação oficial:** os achados mais acionáveis sobre desenvolvimento no FM (reputação de liga não afeta crescimento; drivers são minutos/rating/gap/idade/personalidade) vêm de **testes de comunidade (FM-Arena, thread 13975)**, não de documentação oficial da Sports Interactive — a SI apenas documenta o conceito de "nível apropriado". Tratar como [Prática de comunidade] robusta, mas não oficial.
- **Football Superstar 2 e New Star Manager:** ausência de mecânica documentada de "dificuldade → desenvolvimento" é um achado negativo (não encontrei documentação), não prova de que ela inexista no código.
- **Opta é comercial:** a visão pública de força de liga é gratuita e utilizável como referência agregada, mas o acesso programático completo é pago — planejar a ingestão de dados de acordo.
- **Valores numéricos de Opta, coeficientes e IFFHS citados** referem-se a snapshots de 2025–2026 e mudam a cada temporada (ex.: o nº1 da IFFHS mudou de Serie A italiana em 2024 para Premier League em 2025); usar como estrutura de bandas, não como verdade permanente.
- Todos os rankings medem **força esportiva ou prestígio continental**, não "prestígio" cultural puro — para um mundo paralelo com nações reais, isso é adequado, mas a percepção de prestígio pelo jogador pode divergir dos números (ex.: ligas historicamente grandes hoje em baixa).