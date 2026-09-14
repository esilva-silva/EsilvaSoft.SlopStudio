# Editor, autocomplete, BSON e UUID

## Modos de edição

1. **Consulta e documentos:** um editor textual para Extended JSON/MQL; filtro, projeção, sort, update e pipeline ficam no texto, com autocomplete e diagnóstico. Executa pelo driver.
2. **Comando administrativo:** documento BSON com nome do comando, opções e banco de destino. Usa o registro de capacidades e riscos.
3. **Script JavaScript + JSON:** já possui runner, com consolidação na **v0.8.0**, usa um editor textual próprio para executar lógica JavaScript e queries JSON no mesmo script via processo `mongosh`, em Windows e Linux. Suporta variáveis, funções, condicionais, loops, `await`, múltiplas consultas e operações de escrita autorizadas.

O tipo do editor fica visível na aba e no arquivo salvo. Construtores como `ObjectId(...)`, `UUID(...)`, `ISODate(...)` e `NumberLong(...)` pertencem à sintaxe de shell; datas são exibidas como `ISODate("yyyy-MM-ddTHH:mm:ss.fffZ")`, com segundos, milissegundos e UTC explícito. No editor JSON, snippets geram a representação Extended JSON correspondente. A ferramenta Documentos oferece **Gerar UUID** na representação efetiva da conexão, com o construtor nomeado e o Extended JSON canônico equivalente (seção UUID/GUID abaixo). Um conversor de sintaxe shell, se implementado, aceitará apenas um subconjunto documentado, sem usar `eval`.

## JavaScript e queries JSON na mesma execução

No modo script, o usuário pode declarar um objeto de consulta diretamente no código ou carregar JSON/Extended JSON com `EJSON.parse`. JavaScript prepara o filtro, chama MongoDB e processa o resultado dentro da mesma execução. JSON puro continua disponível no editor textual de consultas. Para números longos, Decimal128, UUID e outros tipos BSON, usar construtores BSON ou Extended JSON; `JSON.parse` comum não restaura esses tipos. [Scripts mongosh](https://www.mongodb.com/docs/mongodb-shell/write-scripts/), [EJSON](https://www.mongodb.com/docs/mongodb-shell/reference/ejson/).

Exemplo de comportamento esperado, a validar nos testes de integração da implementação:

```javascript
// db começa apontando para o banco selecionado na aba.
const idadeMinima = 18;
const filtro = EJSON.parse('{ "ativo": true }');

function aplicarIdade(query, idade) {
    return { ...query, idade: { $gte: idade } };
}

const cursor = db.getCollection("clientes")
    .find(aplicarIdade(filtro, idadeMinima))
    .sort({ "nome": 1, "_id": 1 })
    .limit(100);

while (await cursor.hasNext()) {
    const cliente = await cursor.next();
    printjson(cliente);
}
```

O exemplo usa console nativo. Para resultados em tabela/árvore, a IDE fornecerá uma API própria documentada, `slop.results.emit(documento)`, e `slop.results.stream(cursor)` com limite e cancelamento. Esses helpers serão implementados pelo projeto; não são métodos nativos do mongosh. Chamadas consecutivas podem produzir conjuntos de resultados identificados, além de logs.

Uma aba pode associar parâmetros e uma query em painel JSON opcional, carregados como `slop.input.parameters` e `slop.input.query` no início do job. O usuário também pode escrever todo o JSON dentro do `.js`, como no exemplo. A entrada é serializada em Extended JSON e decodificada pelo runtime; nunca interpolada como código. A fonte recebida é um snapshot e o script pode produzir uma cópia transformada sem alterar o arquivo salvo. O nome `slop` é reservado aos helpers da IDE.

### Contrato de execução

- Execução sequencial no mesmo contexto JavaScript durante o script. Cada job começa com processo/contexto novo; variáveis não vazam para outra aba ou execução. Seleção isolada precisa declarar suas dependências, sem executar um prefixo oculto.
- `db` inicia na conexão/banco da aba. Troca explícita via script segue as regras do shell e permissões do servidor; não prometer confinamento ao banco inicial.
- Usar runner `mongosh --file` com inicialização controlada e configuração pessoal desabilitada (`--norc`); validar flags na versão homologada. Credenciais seguem o canal protegido definido no adaptador, sem constar do script salvo. [Opções mongosh](https://www.mongodb.com/docs/mongodb-shell/reference/options/).
- `print`, `printjson`, stdout e stderr aparecem no console. A API `slop.results` escreve envelopes versionados Extended JSON canônicos em canal próprio de IPC, separado de texto de console; o leitor valida limites/tipos e mantém a UI responsiva. Não tentar extrair resultados estruturados de qualquer texto impresso.
- Erros informam arquivo, linha/coluna, stack e etapa, com mapeamento do wrapper para o código do usuário. Interromper execução restante em erro não tratado; escritas anteriores podem já ter ocorrido.
- Limites de tempo, saída e resultados são configuráveis. Helpers aplicam lotes e contrapressão. JavaScript arbitrário pode chamar `toArray()` e alocar memória: o runner precisa monitorar o processo e encerrá-lo ao ultrapassar o orçamento; não prometer streaming automático de todo código do usuário.
- Cancelar encerra o job e tenta interromper o processo/cursores; efeitos remotos já aplicados permanecem possíveis. Não repetir script automaticamente após falha ou reconexão.
- Processo separado mantém falhas fora da UI, mas não é sandbox de segurança: mongosh executa com permissões locais do usuário e pode acessar arquivos/rede. Scripts gerais em modo somente leitura exigem a política e RBAC descritos no documento de segurança.
- `mongosh` é dependência do modo script, com localização, versão e diagnóstico em Windows/Linux. A v0.8.0 exige instalação/configuração, autenticação e execução reais desse runtime nos sistemas anunciados; ele não é gate obrigatório do MVP v0.5.0.

Autocomplete nesse modo sugere JavaScript, variáveis locais, métodos do shell, coleções e operadores dentro dos objetos MQL. A inferência de variáveis computadas é conservadora; o aceite não exige interpretar todo JavaScript dinamicamente. Salvar/reabrir `.js`, formatos de linha dos dois sistemas e painel JSON associado fazem parte de EDT-06. A implementação atual oferece salvar/abrir por caminho `.js`, seleção gráfica nativa de arquivo e persistência opcional da entrada por caminho: somente objeto JSON de até 64 KiB, nunca o conteúdo do script. A opção começa desmarcada para evitar retenção acidental de dados.

## Pipeline do autocomplete

```text
Texto + posição + modo + contexto imutável da aba
  -> lexer/parser tolerante a texto incompleto
  -> árvore sintática com spans e erros
  -> contexto semântico (filtro/update/pipeline/comando)
  -> catálogo por versão + validator + metadados + amostra em cache
  -> ranking e remoção de sugestões incompatíveis
  -> lista, descrição, tipo, snippet e link oficial
```

O catálogo local mantém nomes, assinaturas, parâmetros, estágio/contexto permitido, versões e depreciações. Não baixar documentação enquanto o usuário digita. A amostragem é cancelável, limitada e informada ao operador; padrão proposto de até 200 documentos, profundidade 12 e orçamento de dois segundos, sem garantia de cobrir o schema. Coleções sensíveis podem desabilitá-la. Inferência não deve recuperar valores distintos apenas para oferecer sugestões de valores.

Ranking proposto: correspondência exata/prefixo, adequação ao contexto, schema explícito, campos observados, frequência e histórico local. Cache separado por conexão, namespace, revisão de credenciais e schema. Usar debounce inicial de 150 ms e descartar respostas cujo snapshot de texto já mudou.

## Casos obrigatórios

| Contexto | Resultado esperado |
| --- | --- |
| `{ "sta` em filtro | Campos compatíveis, com tipos observados e indicação de amostra |
| `{ "age": { "$g` | Operadores de comparação aplicáveis |
| Update no primeiro nível | Operadores de update, não estágios de aggregate |
| Pipeline após `$project` | Campos projetados e aliases inferidos |
| `$group` e estágio seguinte | `_id` e acumuladores definidos, sem afirmar schema completo |
| `$lookup` | Coleção estrangeira e campos do contexto correto |
| `$$` | Variáveis de sistema e variáveis declaradas no escopo |
| `items.$[item]` | Identificadores de arrayFilters declarados |
| Pipeline de update | Apenas estágios permitidos nesse tipo de update |
| Sem conexão | Operadores/snippets locais, sem inventar campos remotos |
| Campo com ponto literal | Distinguir chave literal de caminho; usar operadores apropriados |

Inferência após pipelines complexos é conservadora: apresentar “tipo desconhecido” e permitir texto livre. Completion não substitui validação do servidor. O operador `autocomplete` de MongoDB Search é outra funcionalidade, tratada em ADV-05.

## Fidelidade BSON

O modelo interno mantém `BsonValue` e tipo original. Visualização amigável não altera o documento. Persistência/transferência fiel usa BSON ou Extended JSON canônico; modo relaxed é voltado à leitura humana e sua perda de distinções numéricas deve ser indicada. [Extended JSON](https://www.mongodb.com/docs/manual/reference/mongodb-extended-json/).

| Família | Regra da IDE |
| --- | --- |
| Int32, Int64, Double, Decimal128 | Editores tipados; sem converter tudo para double; preservar NaN/infinito quando representáveis |
| DateTime BSON | Armazenamento UTC em milissegundos; fuso somente na apresentação |
| Timestamp | Exibir componentes de timestamp lógico; não interpretar como DateTime comum |
| ObjectId | Preservar bytes; geração e cópia em formato explícito |
| String, Boolean, Null | Distinguir `null`, campo ausente e string vazia |
| Document e Array | Manter ordem de campos e elementos; navegação preguiçosa |
| Binary | Subtipo e bytes visíveis; base64/hex; subtipo desconhecido permanece opaco |
| Regex | Preservar padrão e opções, sem executar no cliente automaticamente |
| MinKey, MaxKey e tipos legados | Exibir/preservar; criação assistida somente quando apropriada |
| Código BSON, Symbol, Undefined, DBPointer e CodeWithScope legados | Não normalizar silenciosamente; bloquear gravação se não houver representação fiel |

Tipos reconhecidos pelo servidor ou driver mais novo, mas desconhecidos à UI, têm fallback bruto somente leitura até existir codec seguro. Não reconstruir um documento contendo tipo desconhecido mediante simples desserialização JSON. Respeitar o limite de documento BSON e limites anunciados pelo servidor; documentos grandes devem ser inspecionados sem congelar a UI. [Tipos BSON](https://www.mongodb.com/docs/manual/reference/bson-types/), [limites](https://www.mongodb.com/docs/manual/reference/limits/).

## UUID/GUID

MongoDB distingue UUID binário subtype 4 (`Standard`) e formatos legados subtype 3, cujos bytes dependem da origem (`CSharpLegacy`, `JavaLegacy`, `PythonLegacy`). Uma string com aparência de GUID continua sendo string. Não é possível deduzir com segurança a representação legada apenas pelos 16 bytes. [GUID no driver atual](https://www.mongodb.com/docs/drivers/csharp/current/serialization/guids/).

Regras propostas:

- Novos UUIDs binários usam Standard por padrão; UUID v4 inicialmente e geração v7 opcional após fixture de bytes no .NET 10.
- Guardar bytes e subtipo originais; a política por campo/perfil controla interpretação e criação, sem converter toda a coleção.
- Subtype 3 sem origem declarada aparece como binário legado; oferecer interpretações explicitamente rotuladas.
- Escolher uma interpretação para exibição não migra os dados. Migração é job separado com amostra, diff, backup, política de colisão e rollback possível apenas mediante dados preservados.
- Coleções podem misturar representações. Consulta por UUID deve permitir selecionar formato, evitando busca silenciosa em várias representações.
- Mudar `_id` não é update: exige fluxo específico de cópia/exclusão, sujeito a colisões e limites transacionais. Não oferecer “converter todos os IDs” como ajuste visual.
- Não alterar o serializer global por conexão; conexões concorrentes com políticas diferentes devem coexistir.

Exemplo conceitual de filtro C# a validar com a versão fixada:

```csharp
var value = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
var binary = new BsonBinaryData(value, GuidRepresentation.Standard);
var filter = new BsonDocument("customerId", binary);
```

Fixtures independentes devem confirmar bytes standard `00112233445566778899aabbccddeeff`, C# legacy `33221100554477668899aabbccddeeff`, Java legacy `7766554433221100ffeeddccbbaa9988` e Python legacy com bytes standard porém subtype 3. Incluir UUID zero, valores aleatórios, coleções mistas e exportação/importação. O aceite se baseia em bytes esperados, não apenas em serializar e desserializar com o mesmo codec potencialmente errado.

### Representação configurável — implementada em 11/09/2026

`UuidCodec` (Core) é o codec único da IDE; decisão em [ADR-028](10-decisoes-arquiteturais.md#adr-028--uuidguid-configurável-por-preferência-e-conexão-aceita-em-11092026).

| Construtor | Representação | BSON | Texto exibido |
| --- | --- | --- | --- |
| `UUID("…")` | Standard | subtype 4, RFC 4122 | minúsculas |
| `CGUUID("…")` | C# legacy | subtype 3, três primeiros campos invertidos | maiúsculas |
| `JUUID("…")` | Java legacy | subtype 3, cada metade de 8 bytes invertida | minúsculas |
| `GUUID("…")` | Go/Standard (alias) | subtype 4, mesmos bytes de `UUID` | maiúsculas |

- **Entrada:** construtores aceitam 32 dígitos hexadecimais com ou sem hífens, em qualquer caixa. São sempre explícitos: a preferência nunca muda o significado de `UUID(...)` ou de `CGUUID(...)`. Funcionam no Console (funções e `EJSON.parse`), no modo Agregação e nos campos BSON das ferramentas (filtro, documento, update, pipeline) e na importação de arquivos Extended JSON. O modo Script injeta `CGUUID`, `JUUID` e `GUUID` sobre `BinData`/`UUID` nativos do mongosh. Valor inválido gera erro com linha e coluna, antes de qualquer acesso ao servidor.
- **Strings:** texto entre aspas, comentários, expressões regulares e acesso a membro (`x.UUID(...)`) nunca são convertidos. Uma string com aparência de UUID continua string.
- **Saída humana:** Resultados (JSON formatado e árvore), visualização somente leitura, árvore de Documentos, área de transferência, editor de documento e “Abrir no editor” exibem o construtor da representação efetiva. Subtype 4 aparece como `UUID` (ou `GUUID` no perfil Go). Subtype 3 só é decodificado com C# legacy ou Java legacy escolhido explicitamente; com Standard/Go aparece como `$binary` canônico e a métrica informa “UUID(s) legado(s) de origem desconhecida”. Binários de outros subtipos ou tamanhos permanecem canônicos.
- **Fidelidade:** o documento canônico continua sendo a fonte de identidade (`_id`), da precondição de edição e da exportação. Exportar página, exportação lógica e importação usam Extended JSON canônico. Nenhuma preferência reescreve dados armazenados nem altera o serializer global do driver.
- **Política:** preferência global mais sobrescrita por conexão, persistidas em `WorkspacePreferences`. A aba captura um snapshot imutável (`UuidDisplayPolicy`) ao iniciar a execução; resultados de outra conexão usada no Console resolvem a representação dessa conexão. Mudança durante a execução é aplicada quando a aba conclui; resultados ociosos são renderizados novamente a partir do JSON canônico.

Coleções mistas continuam exigindo escolha explícita: os mesmos 16 bytes lidos como C# legacy e Java legacy produzem UUIDs diferentes. Migração de representação permanece fora desta entrega.

### Modo de identificador — implementado em 12/09/2026

`IdentifierRepresentationMode` fica um nível acima da representação UUID e é global; a representação UUID continua separada e pode ser sobrescrita por conexão. Decisão em [ADR-032](10-decisoes-arquiteturais.md#adr-032--modo-de-identificador-acima-da-representação-uuid-12092026).

```text
Modo de identificador
├── ObjectId  → BSON ObjectId
├── UuidV4    → BSON Binary UUID → representação UUID (Standard, C# legacy, Java legacy, Go/Standard)
└── Standard  → ObjectId e UUID, cada um no seu tipo BSON real
```

| Modo | Gerar identificador e `_id` de exemplo | ObjectId na árvore | UUID |
| --- | --- | --- | --- |
| Standard (padrão) | ObjectId e UUID; scripts com `ObjectId("…") /* ou UUID("…") */` | `ObjectId("…")` | construtor da representação |
| ObjectId | `ObjectId("…")` | `ObjectId("…")` | continua UUID, construtor da representação |
| UUID v4 | UUID v4 no construtor da representação | `ObjectId("…") · UUID …` (UUID equivalente) | construtor da representação |

- **Serviço único:** `IdentifierRepresentationService` (Core) concentra `DetectType`, `FormatObjectId`, `FormatUuid`, `ObjectIdToUuid`, `ParseIdentifier`, geração, placeholders de script e a conversão texto humano ⇄ Extended JSON canônico. Bytes e subtipos UUID continuam delegados a `UuidCodec`; a UI não converte valores.
- **Detecção:** com valor BSON (wrapper `$oid` ou `$binary`), vale o tipo real. A inferência textual — 24 dígitos hexadecimais → ObjectId; 32 dígitos com ou sem hífens → UUID na representação configurada — só é usada sem valor BSON, por exemplo em **Interpretar**. Uma string JSON nunca é identificador.
- **Parsing:** aceita `ObjectId("…")` (também `new ObjectId('…')`), 24 hexadecimais, `UUID`/`CGUUID`/`JUUID`/`GUUID("…")`, UUID com 32 dígitos e wrappers canônicos. O modo nunca rejeita um identificador explícito válido. Inválidos informam o motivo e, em construtores, linha e coluna.
- **ObjectId → UUID:** representação alternativa determinística e reversível: os 12 bytes do ObjectId seguidos de 4 bytes zero, lidos na ordem RFC 4122. `66e3bd5b3bc3f54c840d73ac` → `66e3bd5b-3bc3-f54c-840d-73ac00000000`. Não é UUID v4 e nunca substitui o ObjectId em texto reinterpretável; aparece na árvore e na identidade do documento em UUID v4, na prévia, em **Interpretar** e em **Copiar UUID equivalente do _id** (somente UUID v4).
- **Saída humana:** JSON formatado, visualização somente leitura, **Copiar JSON**, Documentos, editor e “Abrir no editor” exibem `ObjectId("…")` em vez de `{"$oid":"…"}` em todos os modos, e UUIDs pelo construtor. `IdentifierRepresentationService.RewriteConstructors` volta ao canônico nos campos BSON, na importação e em `EJSON.parse` do Console, com bytes idênticos. `UuidCodec.FormatForDisplay` e `UuidCodec.RewriteConstructors` continuam restritos a UUID.
- **Menu do documento:** **Copiar _id** (valor com o tipo gravado) e **Copiar consulta por _id** (`db.getCollection("…").find({ _id: … })`) quando há `_id` e coleção conhecida.
- **Persistência:** `WorkspacePreferences.IdentifierMode`, aditivo e textual. Sessões anteriores recebem Standard e mantêm `UuidRepresentation` exatamente como estava — `UuidRepresentation = Standard` não é interpretado como modo. Valor desconhecido torna a sessão ilegível, fica visível e não é sobrescrito. O modo entra no snapshot `UuidDisplayPolicy` capturado pela aba.

Fora desta entrega: migração de `_id` entre tipos, UUID v7, modo por conexão e uma consulta que procure ao mesmo tempo o ObjectId e seu UUID equivalente.

## Escrita concorrente e campos difíceis

Edição comum gera patch de campos realmente alterados. Para concorrência otimista, filtro inclui `_id`, shard key quando necessária e os valores originais dos campos relevantes com tratamento exato de ausência/null/tipo; predicados adicionais devem distinguir casos em que igualdade MongoDB não distingue tipos numéricos. Tokens de versão podem ser usados quando já existem; não inserir campo de controle em documentos do usuário sem opção explícita.

Se a condição não for satisfeita, mostrar conflito e versão remota; jamais interpretar `MatchedCount=0` como sucesso. Comparar antes de gravar sem condição atômica não protege contra corrida. Arrays reordenados e campos com `.`/`$` exigem estratégia específica; oferecer fallback de substituição integral apenas após leitura completa e precondição adequada. Sem precondição exata viável, explicar a limitação e exigir escolha consciente de sobrescrita.

Desfazer local altera apenas o buffer. Após persistir, desfazer é outra escrita sujeita a concorrência, não rollback garantido.

## Implementação desta revisão (10/09/2026)

Abas independentes e editor textual Avalonia existente, código 14/21 configurável e resultados abaixo em Extended JSON. Consultas não usam popup de opções nem campos auxiliares: o texto do editor é a fonte única, com autocomplete e diagnóstico. O contrato de script recebe banco explícito como contexto; `getSiblingDB` é inicializado com serialização de literal, sem alterar authSource. Seleção é enviada integralmente; falha nunca dispara conteúdo alternativo.

F5 executa tudo; Ctrl+Enter executa seleção ou tudo; Ctrl+Espaço solicita sugestões; F6 permite sair do editor; Ctrl+Tab alterna abas. Busca avançada e folding continuam pendentes; highlighting e resultados JSON/árvore já têm implementação, conforme o inventário atual. Rascunhos recuperam texto/contexto; políticas de recuperação e entrada JSON estão no [design system](17-design-system-ui-ux.md).
