import {
  deleteApiV1TradesById,
  getApiV1AuthInfo,
  getApiV1Dashboard,
  getApiV1Instruments,
  getApiV1InstrumentsByTickerBars,
  getApiV1McpCalls,
  getApiV1McpInfo,
  getApiV1Notes,
  getApiV1RefreshRuns,
  getApiV1RefreshRunsById,
  getApiV1Settings,
  getApiV1Trades,
  getApiV1Watchlist,
  postApiV1Instruments,
  postApiV1Notes,
  postApiV1Refresh,
  postApiV1Sizer,
  postApiV1Trades,
  postApiV1TradesByIdCancel,
  postApiV1TradesByIdClose,
  postApiV1TradesByIdFill,
  putApiV1InstrumentsByTicker,
  putApiV1Settings,
  putApiV1TradesById,
} from "./generated/sdk.gen";
import type {
  AuthInfoDto,
  BarsDto,
  CloseTradeRequest,
  CreateInstrumentRequest,
  CreateNoteRequest,
  CreateTradeRequest,
  DashboardDto,
  FillTradeRequest,
  InstrumentDto,
  McpCallDto,
  McpInfoDto,
  NoteDto,
  NoteKind,
  PagedResultOfRefreshRunDto,
  RefreshRunDto,
  SettingsDto,
  SizerRequest,
  SizerResultDto,
  TradeDto,
  UpdateInstrumentRequest,
  UpdateSettingsRequest,
  UpdateTradeRequest,
  WatchlistRowDto,
} from "./generated/types.gen";
import "./client";

// Thin wrappers over the generated client. Each one throws the AxiosError on failure (throwOnError), which the
// slices turn into an ApiProblem. They return exactly what the API returned.
const t = { throwOnError: true } as const;

export const swingTradeApi = {
  authInfo: async (): Promise<AuthInfoDto> => (await getApiV1AuthInfo({ ...t })).data,

  watchlist: async (): Promise<WatchlistRowDto[]> => (await getApiV1Watchlist({ ...t })).data,
  bars: async (ticker: string, days = 120): Promise<BarsDto> =>
    (await getApiV1InstrumentsByTickerBars({ ...t, path: { ticker }, query: { Days: days } })).data,
  instruments: async (): Promise<InstrumentDto[]> => (await getApiV1Instruments({ ...t })).data,
  createInstrument: async (body: CreateInstrumentRequest): Promise<InstrumentDto> =>
    (await postApiV1Instruments({ ...t, body })).data,
  updateInstrument: async (ticker: string, body: UpdateInstrumentRequest): Promise<InstrumentDto> =>
    (await putApiV1InstrumentsByTicker({ ...t, path: { ticker }, body })).data,

  trades: async (): Promise<TradeDto[]> => (await getApiV1Trades({ ...t })).data,
  createTrade: async (body: CreateTradeRequest): Promise<TradeDto> => (await postApiV1Trades({ ...t, body })).data,
  updateTrade: async (id: number, body: UpdateTradeRequest): Promise<TradeDto> =>
    (await putApiV1TradesById({ ...t, path: { id }, body })).data,
  deleteTrade: async (id: number): Promise<void> => {
    await deleteApiV1TradesById({ ...t, path: { id } });
  },
  fillTrade: async (id: number, body: FillTradeRequest): Promise<TradeDto> =>
    (await postApiV1TradesByIdFill({ ...t, path: { id }, body })).data,
  closeTrade: async (id: number, body: CloseTradeRequest): Promise<TradeDto> =>
    (await postApiV1TradesByIdClose({ ...t, path: { id }, body })).data,
  cancelTrade: async (id: number): Promise<TradeDto> => (await postApiV1TradesByIdCancel({ ...t, path: { id } })).data,

  size: async (body: SizerRequest): Promise<SizerResultDto> => (await postApiV1Sizer({ ...t, body })).data,

  dashboard: async (): Promise<DashboardDto> => (await getApiV1Dashboard({ ...t })).data,

  notes: async (kind?: NoteKind): Promise<NoteDto[]> =>
    (await getApiV1Notes({ ...t, query: kind ? { Kind: kind } : undefined })).data,
  createNote: async (body: CreateNoteRequest): Promise<NoteDto> => (await postApiV1Notes({ ...t, body })).data,

  settings: async (): Promise<SettingsDto> => (await getApiV1Settings({ ...t })).data,
  updateSettings: async (body: UpdateSettingsRequest): Promise<SettingsDto> =>
    (await putApiV1Settings({ ...t, body })).data,

  startRefresh: async (): Promise<RefreshRunDto> => (await postApiV1Refresh({ ...t })).data,
  refreshRun: async (id: number): Promise<RefreshRunDto> =>
    (await getApiV1RefreshRunsById({ ...t, path: { id } })).data,
  refreshRuns: async (pageSize = 1): Promise<PagedResultOfRefreshRunDto> =>
    (await getApiV1RefreshRuns({ ...t, query: { Page: 1, PageSize: pageSize } })).data,

  mcpInfo: async (): Promise<McpInfoDto> => (await getApiV1McpInfo({ ...t })).data,
  mcpCalls: async (take = 20): Promise<McpCallDto[]> => (await getApiV1McpCalls({ ...t, query: { take } })).data,
};
