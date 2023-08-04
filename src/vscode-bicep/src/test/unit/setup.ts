// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// WARNING: The unit tests run in parallel, so this mock is a singleton and shared among all the unit tests.
// So don't use things like expect().toHaveBeenCalledTimes() in the unit tests, because they will interfere with each other.
//
// (This is not the case for the e2e tests, which run sequentially.)

// eslint-disable-next-line no-debugger
debugger;

import * as vscodeUri from "vscode-uri";

jest.mock(
  "vscode",
  () => ({
    $$_this_is_a_mock_$$: "see vscode/src/test/unit/setup.ts",
    ConfigurationTarget: {
      Global: 1,
      Workspace: 2,
      WorkspaceFolder: 3,
    },
    languages: {
      createDiagnosticCollection: jest.fn(),
      registerCodeLensProvider: jest.fn(),
    },
    ProgressLocation: {
      Notification: 15,
      SourceControl: 1,
      Window: 10,
    },
    StatusBarAlignment: { Left: 1, Right: 2 },
    ThemeColor: jest.fn(),
    ThemeIcon: jest.fn(),
    window: {
      createStatusBarItem: jest.fn(() => ({
        show: jest.fn(),
        tooltip: jest.fn(),
      })),
      showErrorMessage: jest.fn(),
      showWarningMessage: jest.fn(),
      createTextEditorDecorationType: jest.fn(),
      createOutputChannel: jest.fn(),
      showWorkspaceFolderPick: jest.fn(),
      onDidChangeActiveTextEditor: jest.fn(),
      showInformationMessage: jest.fn(),
    },
    workspace: {
      getConfiguration: jest.fn(),
      workspaceFolders: [],
      getWorkspaceFolder: jest.fn(),
      onDidChangeConfiguration: jest.fn(),
      onDidChangeTextDocument: jest.fn(),
      onDidChangeWorkspaceFolders: jest.fn(),
    },
    OverviewRulerLane: {
      Left: null,
    },
    Uri: {
      parse: jest.fn((uri) => {
        return vscodeUri.URI.parse(uri);
      }),
      file: jest.fn((path) => {
        return vscodeUri.URI.file(path);
      }),
    },
    Range: jest.fn(),
    Diagnostic: jest.fn(),
    DiagnosticSeverity: { Error: 0, Warning: 1, Information: 2, Hint: 3 },
    debug: {
      onDidTerminateDebugSession: jest.fn(),
      startDebugging: jest.fn(),
      registerDebugConfigurationProvider: jest.fn(),
    },
    commands: {
      executeCommand: jest.fn(),
      registerCommand: jest.fn(),
    },
    CodeLen: jest.fn(),
    l10n: {
      t: jest.fn(),
    },
  }),
  { virtual: true },
);
