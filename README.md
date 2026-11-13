# SteamBridge

SteamBridge é um aplicativo **só para Windows** que reúne, numa tela só, os jogos instalados da Steam, da Epic Games, da Xbox / Game Pass e de pastas locais. Ele cria atalhos não-Steam, organiza coleções e troca a arte da biblioteca: capa vertical, capa horizontal, banner, logo e ícone.

A versão que se usa no dia a dia é a interface WPF **SteamBridge.Desktop**. O código também guarda uma interface WinForms mais antiga; ela não é o programa do zip.

Versão atual: **3.0.0**. Licença MIT.

Download: [SteamBridge 3.0.0](https://github.com/SirSh4dow/SteamBridge/releases/tag/v3.0.0)

---

## Baixar e abrir

1. Baixe somente [SteamBridge-portable.zip](https://github.com/SirSh4dow/SteamBridge/releases/download/v3.0.0/SteamBridge-portable.zip).
2. Feche qualquer SteamBridge que já esteja aberto.
3. Extraia o zip. Dentro existem **dois** arquivos, e os dois precisam ficar na mesma pasta:

| Arquivo | O que é | Você clica? |
|---|---|---|
| `SteamBridge.Desktop.exe` | O aplicativo. Biblioteca, capas, coleções e configurações. | **Sim. É este que se abre.** |
| `SteamBridgeLauncher.exe` | Auxiliar minúsculo. O Steam chama esse arquivo, por linha de comando, para iniciar jogo da Xbox (e alguns da Epic) e contar o tempo de jogo. | **Não.** Aberto com duplo clique ele não recebe o jogo, o modo nem os argumentos, e mostra erro de uso. Isso é esperado. |

O zip já traz o .NET embutido. Não é preciso instalar o runtime.

Se a pasta de onde o programa foi aberto tiver rótulo de integridade baixa do Windows, o Steam iniciado por ele não consegue gravar o próprio caminho no registro e passa a exibir avisos. Nesse caso use `tools/install-local.bat` depois de compilar, ou o atalho do Menu Iniciar que esse script cria em `%USERPROFILE%\SteamBridge`. O `run.bat` da raiz também prefere essa cópia.

---

## O que dá para fazer

### Minha biblioteca

- Lista os jogos da conta Steam selecionada no canto superior.
- **Escanear jogos** procura, além da Steam:
  - Epic, pelos manifestos `.item` em `%ProgramData%\Epic\EpicGamesLauncher\Data\Manifests`;
  - Xbox / Game Pass, por um script do PowerShell que lê `Get-AppxPackage` e o `MicrosoftGame.Config` (o caso `GameLaunchHelper.exe` dos jogos GDK);
  - pastas locais, até três níveis, ignorando instaladores, cracks, redistribuíveis, editores e pastas de sistema.
- **Adicionar jogo** aceita um arquivo `.exe`, `.cmd` ou `.bat` escolhido na hora. O título inicial é o nome do arquivo; dá para trocar em **Detalhes**.
- Filtros: Todos, Steam, Epic, Xbox e Locais. A busca ignora acentos.
- **Integrar ao Steam** grava os selecionados em `shortcuts.vdf`. Jogos que já são atalho não entram de novo. Um conflito de AppID pede para renomear o jogo antes de gravar.
- **Detalhes** mostra e edita título, executável, argumentos e pasta de trabalho. O nome de um jogo nativo da Steam não é alterado por aqui.
- **Remover atalhos selecionados** (em Configurações) apaga só o atalho. Não desinstala o jogo.

O AppID de um atalho é o mesmo cálculo que a Steam faz a partir do executável (entre aspas, quando preciso) e do nome. É esse número que liga a capa ao jogo.

Jogos Xbox não são abertos pelo `.exe` direto. O atalho aponta para o `SteamBridgeLauncher.exe` com `--aumid "Pacote!App"` e, quando existe, `--wait-exe nome.exe`, para o processo continuar vivo e a Steam contar o tempo. Jogos Epic que só têm URI de launcher usam `--uri`.

### Artes e capas

A primeira tela é a prateleira: a grade das suas capas. Clicar num jogo abre o estúdio, com **← Suas capas** para voltar.

No estúdio há cinco formatos. A miniatura e a prévia mudam de proporção para a imagem inteira aparecer; só a capa vertical fica alta.

| Formato na tela | Arquivo na pasta `grid` | Para que a Steam usa |
|---|---|---|
| Vertical | `{appid}p.png` | Capa em pé da biblioteca |
| Horizontal | `{appid}.png` | Capa deitada |
| Banner | `{appid}_hero.png` | Fundo largo da página do jogo |
| Logo | `{appid}_logo.png` | Logo por cima do banner |
| Ícone | `{appid}_icon.png` | Ícone pequeno |

Extensões aceitas na pasta: `.png`, `.jpg`, `.jpeg`, `.ico`, `.webp`. Ao gravar um formato, as outras extensões daquele mesmo nome são removidas, depois de backup.

**Busca online.** Com a chave da SteamGridDB, o estúdio procura o jogo pelo nome, deixa escolher outra correspondência se houver mais de um resultado e mostra até 18 opções. Clicar numa opção só atualiza a prévia. **Aplicar capa** é que baixa a imagem inteira e grava.

**Importar** usa um PNG ou JPEG do disco, até 25 MB. Não precisa de chave.

**Ícone é um caso à parte.** A Steam atual não mostra o ícone pequeno só pelo arquivo da pasta `grid`. Ela lê `Steam\appcache\librarycache\{appid}\`, em geral um arquivo cujo nome contém `icon`. O SteamBridge:

1. regrava a imagem como PNG de verdade em `{appid}_icon.png` na pasta `grid`;
2. substitui, no mesmo nome, o arquivo de ícone que já existe no `librarycache` (JPEG se o arquivo for `.jpg` / `.jpeg`, PNG nos outros);
3. se ainda não existir arquivo com "icon" no nome, cria `{appid}_icon.jpg`;
4. se o jogo for um atalho não-Steam, aponta o campo `icon` do `shortcuts.vdf` para o PNG da pasta `grid`.

Feche o Steam por completo e abra de novo. Trocar o nome do arquivo do cache faz a Steam baixar o ícone original de volta; por isso a gravação reutiliza o nome que já está lá.

### Coleções

Lista as coleções da conta. Dá para criar uma coleção, marcar jogos já integrados e salvar. Coleção dinâmica (filtro da própria Steam) aparece como somente leitura. A exclusão remove a coleção, não os jogos. Jogos que existem na coleção mas não estão visíveis nesta biblioteca são preservados na hora de salvar.

### Configurações

- Pasta da Steam. Em branco, o programa detecta sozinho: registro `HKCU\Software\Valve\Steam`, depois `HKLM\SOFTWARE\WOW6432Node\Valve\Steam`, depois `C:\Program Files (x86)\Steam` e `C:\Program Files\Steam`.
- Pastas de jogos locais. Não aponte a raiz do disco. A varredura desce no máximo três níveis e pula `Windows`, `Program Files`, `steamapps` e pastas de ferramenta.
- Chave da SteamGridDB. Opcional. Sem ela, importar imagem continua funcionando; a busca online não. A chave fica em `%AppData%\SteamBridge\desktop-config.json`. Não compartilhe esse arquivo.
- Atalho para a pasta de backups, restaurar um `shortcuts.vdf` e remover atalhos selecionados.

A conta ativa é a mais recente do `config\loginusers.vdf`, ou a que você escolher no topo. Trocar de conta recarrega a biblioteca.

---

## Steam fechado, de propósito

Gravar atalho, coleção ou arte exige o Steam **fechado**. O programa não mata o processo `steam.exe`.

O botão **Fechar Steam** pede confirmação e envia `steam.exe -shutdown`. Espera até 30 segundos. Se o cliente não sair, nada é encerrado à força: finalize downloads e jogos e use Steam → Sair.

**Abrir Steam** usa `steam://open/main` e espera a interface (`steamwebhelper`) por até 30 segundos. O programa não reabre o Steam sozinho depois de uma gravação.

Antes de substituir um arquivo que já existe, uma cópia vai para `userdata\<id3>\config\backups_steambridge`. Se o `shortcuts.vdf` estiver corrompido ou incompleto, a gravação é recusada em vez de ser substituída por uma lista nova.

---

## Onde os arquivos ficam

| O quê | Caminho |
|---|---|
| Configuração do SteamBridge | `%AppData%\SteamBridge\desktop-config.json` |
| Atalhos não-Steam | `<Steam>\userdata\<id3>\config\shortcuts.vdf` |
| Capas, hero, logo e ícone de grade | `<Steam>\userdata\<id3>\config\grid\` |
| Ícone pequeno que a biblioteca mostra | `<Steam>\appcache\librarycache\<appid>\` |
| Backups | `<Steam>\userdata\<id3>\config\backups_steambridge\` |
| Coleções | arquivos de coleção do usuário, via `CollectionsManager` |
| Cópia recomendada do app | `%USERPROFILE%\SteamBridge\` |

`id3` é o número da pasta da conta dentro de `userdata` (a parte baixa do SteamID64).

Uma configuração antiga da versão em Python ainda é lida, se não houver o campo novo: `sgdb_key`, `steam_path` e `roots` dentro de `config.json` na mesma pasta.

A variável `STEAMBRIDGE_HOME` troca a pasta de configuração. Quando ela está definida, o programa não vasculha as unidades `Games` e `Jogos` sozinho.

---

## Chave da SteamGridDB

1. Crie uma conta em [steamgriddb.com](https://www.steamgriddb.com).
2. Abra [Profile → Preferences → API](https://www.steamgriddb.com/profile/preferences/api).
3. Cole a chave em **Configurações → SteamGridDB** e salve.

A busca de capa vertical e horizontal pede dimensões à API. Ícone usa o endpoint `icons/game`, sem esse filtro.

---

## Compilar

Requisitos: Windows 10 ou 11, 64 bits, e o SDK .NET 8.

```bat
build-native.bat
```

O script roda as checagens dos projetos em `tests\` e publica um executável autocontido em `publish-native\`:

```bat
dotnet publish src\SteamBridge.Desktop\SteamBridge.Desktop.csproj -c Release -r win-x64 --self-contained true -o publish-native
```

O launcher auxiliar não usa o SDK. Ele é compilado com o `csc.exe` do .NET Framework que já vem no Windows:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File launcher\build.ps1
```

A saída é `launcher\bin\SteamBridgeLauncher.exe`. O projeto Desktop copia esse arquivo para junto do programa. Sem ele, atalho de Xbox não é criado.

Para instalar fora de uma pasta com integridade baixa:

```bat
tools\install-local.bat
```

Isso copia `publish-native` para `%USERPROFILE%\SteamBridge`, confere o SHA-256 de cada arquivo e cria o atalho **SteamBridge** no Menu Iniciar do seu usuário. A cópia anterior, se existir, é renomeada, não apagada. O script não altera permissão de arquivo nem mexe em nada da Steam.

---

## Mapa do repositório

```
SteamBridge/
├── build-native.bat          publica o app e roda as checagens
├── run.bat                   abre a cópia em %USERPROFILE%\SteamBridge
├── launcher/
│   ├── SteamBridgeLauncher.cs    auxiliar de Xbox/URI; não é a janela do app
│   └── build.ps1                 compila o launcher com o csc do Windows
├── tools/
│   ├── install-local.ps1     copia verificada + atalho do Menu Iniciar
│   └── install-local.bat
├── src/SteamBridge.Desktop/  interface que o zip abre
│   ├── MainWindow.xaml       layout: biblioteca, prateleira, estúdio, coleções
│   ├── MainWindow.xaml.cs    ações da janela, busca de arte e gravação
│   ├── App.xaml              tema escuro, botões, filtros e cartões
│   ├── LibraryService.cs     varredura, atalhos, renomear, lançar jogo
│   ├── GameItem.cs           um jogo na grade, com a capa
│   ├── GameDetailsWindow.cs  diálogo de título, exe, argumentos e pasta
│   └── NativeWindow.cs       moldura escura da janela
├── src/SteamBridge/          núcleo compartilhado e a UI WinForms antiga
│   ├── Core/
│   │   ├── SteamLocator.cs       achar a Steam, usuários, bibliotecas; abrir e fechar
│   │   ├── SteamArtManager.cs    pasta grid, sufixos e ícone do librarycache
│   │   ├── SteamGridDbClient.cs  busca, artes e download
│   │   ├── ShortcutsVdf.cs       ler e gravar shortcuts.vdf sem perder campos desconhecidos
│   │   ├── CollectionsManager.cs coleções da conta
│   │   ├── SafeFile.cs           backup e gravação atômica
│   │   ├── AppConfig.cs          desktop-config.json
│   │   ├── SteamInstalledScanner.cs  jogos instalados nas bibliotecas Steam
│   │   ├── SteamUtils.cs         AppID, aspas e caminhos
│   │   └── VdfTextParser.cs      VDF de texto (loginusers, libraryfolders)
│   ├── Scanners/
│   │   ├── EpicScanner.cs
│   │   ├── XboxScanner.cs        PowerShell + MicrosoftGame.Config
│   │   └── FolderScanner.cs      .exe local, profundidade 3
│   ├── Models/DataModels.cs      SteamInfo, atalho, jogo, coleção, arte
│   ├── UI/                       interface WinForms anterior (não é o exe do zip)
│   ├── Program.cs                entrada dessa interface antiga
│   └── app.manifest              pedido de execução sem elevação
└── tests/                    projetos de checagem usados pelo build-native.bat
```

`SteamBridge.Desktop.csproj` não duplica o núcleo: ele compila `Core`, `Models` e `Scanners` direto da pasta `src/SteamBridge`.

### Quem chama quem

1. A janela pede a biblioteca a `LibraryService`.
2. `LibraryService` junta jogos instalados da Steam, atalhos já gravados e, se você escanear, Epic, Xbox e pastas. Jogos manuais entram pela lista salva na configuração.
3. Integrar chama `CreateShortcut` e `ShortcutsVdf.Write`, só com o Steam fechado.
4. Aplicar uma arte chama `SteamArtManager.SaveArt`. No formato ícone, a imagem é reencodada e `PublishLibraryIcon` atualiza o cache; atalho não-Steam também recebe o caminho no campo `icon`.
5. Coleções passam por `CollectionsManager`, também com o Steam fechado.

A interface WinForms (`src/SteamBridge/UI`) repete biblioteca, arte, coleções e configurações em outra tecnologia. O zip e o atalho do Menu Iniciar abrem só o WPF.

---

## Limites conhecidos

- Só Windows. O código usa registro, PowerShell, `Appx` e a Steam local.
- Arte e coleção só aparecem depois que o cliente da Steam inicia de novo.
- O ícone depende do nome do arquivo que já está no `librarycache`. Se a Steam guardar esse ícone com um nome que não contém `icon`, o arquivo antigo pode continuar na tela até o cliente ser reiniciado ou o cache daquele app ser refeito.
- A varredura de pasta não desce mais de três níveis e pode eleger o `.exe` errado numa pasta com várias ferramentas. O diálogo **Detalhes** existe para corrigir o executável.
- O programa não fecha a Steam à força e não altera ACL de pasta.

---

## Licença

[MIT](LICENSE). Copyright (c) 2026 SteamBridge contributors.
