# Variações KapibaraStudio

Os arquivos `kapibara-logo-light.png` e `kapibara-logo-dark.png` são PNGs RGBA transparentes de 1448 × 1086 pixels. Compartilham a mesma máscara de transparência, posição e margens. Os arquivos `kapibara-preview-*.png` mostram esses assets sobre branco e azul-marinho #101827.

Foi usada a ferramenta integrada ImageGen para produzir variações a partir do anexo. As tentativas de transparência geraram artefatos nas letras claras e foram descartadas. Com autorização explícita do usuário, os arquivos finais foram preparados localmente a partir da referência original usando Pillow e NumPy, preservando a geometria e recolorindo a tipografia. Não houve integração com o aplicativo.

## Direção dos prompts

- Light: preservar capivara/banco de dados, gradiente violeta–azul–ciano, composição e tipografia; texto exato KapibaraStudio; Kapibara #08143F e Studio #3A4C76; remover fundo branco, com transparência real e sem halos.
- Dark: preservar os mesmos elementos, dimensões e margens; alterar apenas Kapibara para #F4F7FF e Studio para #C1CEE5; fundo transparente, bordas limpas, sem contornos ou brilho.
- Prévia dark: mesmos elementos sobre fundo #101827, com as faixas negativas do banco de dados acompanhando o fundo.

## Validação

Inspeção visual das duas prévias finais; dimensões, máscara alfa idêntica e transparência real verificadas pelo script. A referência contém o desenho raster original; os arquivos não são vetoriais.

Para reproduzir: executar `prepare_logos.py` com Python, Pillow e NumPy. A entrada está preservada em `kapibara-reference.png`.
