# Research Prompt — Pipeline de Personagem Modular

> **⚠️ STATUS ATUALIZADO — ver `Terra_Paralela_Registro_Decisoes_Pivo2D_Personagem.md`.** Este documento foi escrito assumindo Opção B (3D em tempo real). **D-01 trava a trilha canônica como 2D**; a via 3D descrita nos Blocos A–D abaixo está **arquivada/dormente** (mesmo tratamento do repo `testingclaude`) — preservada para uma eventual revisão futura de direção de arte, não para execução agora. **Blocos F e G permanecem ativos** (independentes de renderização). **Bloco H (novo)** cobre o pipeline 2D que substitui A–D na prioridade atual.
>
> **Uso:** executar em IA com acesso à web. Saída alimenta a futura extensão 2D do módulo de aparência (bloqueada por D-08 até o steering passar no harness).
> **Convenção:** responder em prosa PT-BR; identificadores, nomes de nós e termos técnicos em inglês.

---

## Contexto do projeto (leia antes de pesquisar) — ⚠️ trilha 3D, arquivado, ver banner acima

Estou desenvolvendo **Terra Paralela**, um Soccer Life Simulator em **Godot 4 com C#**, renderização **3D em tempo real**, alvo desktop com footprint leve. A arquitetura de aparência já está decidida e **não deve ser questionada** — a pesquisa serve para preencher lacunas de implementação, não para propor alternativas:

- Aparência é um struct serializável (`AppearanceProfile`): índices de partes por slot (cabeça, cabelo, barba, etc.) + cores (pele, cabelo, olhos) via material parameters.
- **Um único esqueleto humanoide compartilhado** por todos os personagens; animações universais; meshes skinnadas trocáveis por slot em runtime.
- Um único `AppearanceAssembler` monta o modelo a partir do profile. NPCs gerados proceduralmente usam o mesmo pipeline (seed-determinístico).
- Máximo de **22 modelos simultâneos** em cena (uma partida); fora da partida, personagens são consumidos via retrato renderizado em `SubViewport` com cache PNG.
- Princípios do projeto: um caminho só, sem fallbacks, fail-fast, simple > complex.

**Objetivo da pesquisa:** estabelecer o pipeline de autoria e importação **Blender → Godot 4** para personagens modulares, com parâmetros e decisões fundamentados em prática real da comunidade e documentação oficial — não em chute.

---

## ⚠️ Blocos A–D — ARQUIVADOS (trilha 3D, dormente sob D-01)

O conteúdo abaixo (Blocos A–D) pressupõe rig 3D, export Blender→glTF e troca de mesh skinnada — nenhum disso se aplica à trilha 2D canônica. Mantido no arquivo por valor histórico e para o caso de uma futura revisão de direção de arte reabrir D-01 (não é o cenário atual). **Não disparar estes blocos como pesquisa ativa.**

## Bloco A — Autoria no Blender (rig único, partes modulares)

1. Qual é o workflow recomendado para autorar **múltiplas meshes modulares skinnadas a um único armature** no Blender, de modo que todas as partes (torso, cabeças, cabelos, etc.) compartilhem exatamente o mesmo rig? Um `.blend` único com todas as partes vs. arquivos separados referenciando um rig linkado — prós, contras e o que a comunidade Godot usa em produção.
2. Convenções de **naming e organização** (bones, vertex groups, meshes, materials) que sobrevivem à importação glTF sem renomeação silenciosa.
3. Rig recomendado: **Rigify** (com ou sem game-rig/deform-only export), **UEFN/Mixamo-style**, ou rig manual mínimo? Critério: compatibilidade com `SkeletonProfileHumanoid` do Godot e retargeting futuro de animações de terceiros.
4. Quantos bones um rig humanoide de futebol precisa na prática (sem dedos individuais? com twist bones?) considerando 22 modelos animados simultâneos em hardware modesto.
5. Como autorar **cabelos/barbas/acessórios rígidos** presos à cabeça: skinning total ao head bone vs. mesh separada para uso com `BoneAttachment3D` — qual o critério de escolha e o custo de cada um.

## Bloco B — Exportação e importação (glTF, o contrato Blender→Godot)

6. Configuração de **exportação glTF 2.0** do Blender para personagens skinnados: flags que importam (apply modifiers, deform bones only, rest position, tangentes), armadilhas conhecidas (escala 0.01, +Y up, bone roll) e como validar que a exportação está correta antes de abrir o Godot.
7. No Godot 4 (versão estável atual — verificar qual é), qual o **fluxo de importação recomendado** para este caso: importar como cena única e extrair meshes, ou importar partes como `.glb` separados? Como configurar o import para que as meshes reusem o mesmo `Skeleton3D` em runtime.
8. **O gotcha central:** ao trocar uma mesh skinnada em runtime (reparentar/instanciar `MeshInstance3D` sob um `Skeleton3D` já existente), o que exatamente precisa bater para não haver deformação — a resource `Skin`, os nomes dos bones, o rest pose? Documentar o procedimento correto e os erros clássicos (mesh warping, T-pose congelada, offset de escala). Buscar tanto a documentação oficial quanto threads de fórum/GitHub com soluções confirmadas.
9. `SkeletonProfile` / `SkeletonProfileHumanoid` / `BoneMap`: quando são necessários neste pipeline (rig próprio + animações próprias) e quando são apenas para retargeting de animações externas? Vale adotar o profile humanoid desde o início como seguro para o futuro?

## Bloco C — Materiais, cores e variação

10. Melhor prática para **recoloração paramétrica** (pele, cabelo, olhos) em Godot 4: shader parameters por instância (`instance uniforms`), material duplicado por personagem, ou máscara de canais (RGB mask) num material compartilhado? Critério: 22 personagens simultâneos + kits de clube trocáveis, minimizando draw calls e materiais únicos.
11. Como estruturar a textura/UV das partes para suportar recoloração por máscara sem retrabalho de arte por variação (padrão comum em jogos de esporte e customização).
12. Existe custo relevante de `instance uniforms` no renderer Forward+ vs. Mobile? O projeto quer footprint leve — qual renderer e qual técnica de recolor combinam melhor.

## Bloco D — Performance e validação

13. Benchmarks ou relatos práticos: 22 personagens skinnados animados (com `AnimationTree`) em Godot 4 — quantos bones/vértices por personagem são confortáveis em GPU integrada? Existe ganho real em LOD de mesh (`ImporterMesh` LODs automáticos) para essa escala ou é otimização prematura?
14. Custo de renderizar retratos via `SubViewport` sob demanda (modelo + câmera no rosto): alguma armadilha conhecida (lighting da viewport, MSAA, transparência de fundo para PNG)?
15. Como **validar automaticamente** no boot que todas as partes do catálogo são compatíveis com o esqueleto compartilhado (mesmos bones, mesma escala) — existe API para comparar `Skin`/skeleton, ou a validação deve ser feita no pipeline de import (script de import custom)?

## Bloco E — Autoria incremental e ferramentas

16. Existe tooling da comunidade (addons Blender, plugins Godot) consolidado para pipeline de personagem modular que valha adotar — ou o custo de dependência supera o benefício para um catálogo pequeno (MVP: 3–4 slots)? Avaliar apenas opções com licença permissiva e manutenção ativa.
17. Character bases prontas com licença permissiva (CC0/MIT) que sirvam como ponto de partida do rig e das proporções (ex.: bases humanas low-poly conhecidas na comunidade) — listar candidatas com link e licença.

## Bloco F — Distribuição procedural por nacionalidade

> O `AppearanceGenerator` alimenta tanto o botão "aleatorizar" do criador do jogador quanto a geração de milhares de NPCs do mundo — a mesma distribuição serve aos dois. Este bloco busca **precedente de estrutura de dados**, não pesos finais.

18. Como jogos de futebol com geração de elenco em massa (FIFA/EA FC, PES/eFootball, Football Manager) documentam ou permitem inferir publicamente que variam **tom de pele, tipo/textura de cabelo, traços faciais** por nacionalidade/região na geração de jogadores genéricos (regens). Buscar documentação de editores, datamining de comunidade, ou entrevistas de desenvolvedores — citando a fonte.
19. Existem boas práticas documentadas (de estúdios, GDC talks, ou pesquisa em geração procedural de personagens) sobre variação demográfica responsável em geradores — especificamente como evitar caricatura ou viés ofensivo ao condicionar aparência por nacionalidade.
20. Nível de granularidade que é prática de mercado real: paletas curadas por macro-região (ex. "Europa Ocidental", "África Subsaariana") vs. por país individual — qual granularidade os jogos de referência realmente usam, e por quê.
21. **Fronteira explícita para a resposta:** o objetivo é a estrutura de dados (formato de tabela, granularidade, forma de composição com o `PartCatalog` existente) e o precedente de mercado — não listar ou gerar estereótipos. Se não houver fonte confiável neste eixo, reportar como lacuna explicitamente, sem preencher por suposição.

## Bloco G — Envelhecimento visual derivado

22. Precedentes de **aparência derivada de atributos de gameplay** (idade) em vez de escolha manual re-editada a cada temporada — ex.: grisalho progressivo, leve alteração de traços faciais — em jogos de carreira longa (Football Manager, Sims, outros). Como é implementado tecnicamente (blend shape/shader por parâmetro contínuo de idade vs. troca discreta de asset ao cruzar um limiar de idade).
23. Existe algum parâmetro contínuo publicamente documentado (ex. "% de cabelo grisalho como função de idade") reaproveitável como ponto de partida, ou isso é território puramente de tuning de design (registrar como lacuna se for o caso)?

---

## Bloco H — Composição de sprite em camadas + palette swap (trilha 2D ativa)

> Substitui A–D como o bloco técnico prioritário. Premissa travada pelo Registro de Decisões do pivô: recoloração via **paleta indexada + troca de paleta em runtime** (técnica clássica 16-bit — kits de time trocados sem redesenhar sprite), não shader de material 3D. Compositing por camadas em **duas resoluções-consumidoras**: sprite de partida (16×16/32×32, GDD §1) e sprite/retrato de vida-sim (isométrico, resolução maior). Um `AppearanceProfile`, dois compositores.

24. Em Godot 4, qual é o padrão recomendado para **compor um personagem 2D em camadas** (corpo base, cabelo atrás, cabeça, cabelo na frente, barba) a partir de sprites independentes — `Sprite2D` empilhados por `z_index` vs. pré-composição para uma única textura cacheada (`SubViewport` 2D ou composição via `Image`/`ImageTexture` em código)? Trade-off para ~22 instâncias simultâneas em partida.
25. **Palette swap em Godot 4:** como implementar troca de paleta indexada em shader 2D (`CanvasItem` shader lendo um sprite em índices de cor + uma textura de paleta) — é o padrão real usado por jogos indie 16-bit modernos em Godot? Buscar exemplos/tutoriais confirmados, não só teoria.
26. Como a mesma técnica de paleta serve **tanto cor de pele/cabelo quanto cor de kit** (`ClubIdentity.palette`) — um shader, dois consumidores, ou dois shaders por convenção de projeto?
27. Convenção de nomenclatura/organização de arquivo para sprites em duas resoluções (partida 16×16/32×32 vs. vida-sim isométrico) do mesmo `PartCatalog` — como evitar que o catálogo vire duas fontes de verdade paralelas por causa da resolução dupla.
28. Pipeline de autoria recomendado (Aseprite → Godot, ou equivalente) para pixel art em camadas com paleta indexada — exportação, importação, e como manter as camadas alinhadas entre si (mesmo pivot/canvas) para que a composição nunca desalinhe entre partes.
29. Existe custo relevante de compositar ~22 personagens por camadas a cada frame em Godot 4 2D, ou pré-composição em cache (uma vez por profile, reaproveitado enquanto o profile não muda) é claramente a escolha certa — mesma disciplina de cache já usada no `CrestGenerator`?

---

## Formato da resposta esperada

- Responder **apenas os Blocos F, G e H** (ativos). Blocos A–D estão arquivados — não pesquisar.
- Por bloco: **recomendação direta primeiro**, depois a justificativa com fontes (documentação oficial do Godot, threads confirmadas, repositórios).
- Distinguir explicitamente o que é **documentado oficialmente** vs. **prática de comunidade** vs. **incerto/controverso**.
- Bloco F exige cuidado redobrado de enquadramento — ver a fronteira explícita no item 21.
- Terminar com uma seção **"Decisões prontas"**: lista objetiva de parâmetros/escolhas transcritíveis diretamente para a extensão do módulo quando D-08 liberar (técnica de compositing, formato do shader de paleta, estrutura de distribuição por nacionalidade, abordagem de envelhecimento).
- Onde não houver consenso ou dado empírico, dizer isso explicitamente — **não inventar números**.
