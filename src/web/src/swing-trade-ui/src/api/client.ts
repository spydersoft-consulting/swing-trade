import axios from "axios";
import { client } from "./generated/client.gen";
import { notifyForbidden, notifyUnauthorized } from "../context/authSession";
import { FORBIDDEN_MESSAGE } from "./problem";
import { getConfig } from "../utils/Config";
import { serializeRequests } from "./requestGate";

// The generated URLs already start with /api/v1; the base is only the origin (empty means same origin, i.e. the BFF).
client.setConfig({ baseURL: getConfig("api_url").replace(/\/api\/v1\/?$/, "") });

serializeRequests(client.instance);

client.instance.interceptors.response.use(
  (response) => response,
  (error: unknown) => {
    const status = axios.isAxiosError(error) ? error.response?.status : undefined;
    if (status === 401) {
      // The BFF session (or the token it holds) is stale: AuthProvider sends the browser through sign-in again.
      notifyUnauthorized();
    } else if (status === 403) {
      notifyForbidden(FORBIDDEN_MESSAGE);
    }
    return Promise.reject(error);
  },
);

export { client };
