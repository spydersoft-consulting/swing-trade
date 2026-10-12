import { defineConfig } from "@hey-api/openapi-ts";

// The input is the OpenAPI document the API build writes (yarn api:spec -> dotnet build -p:GenerateOpenApi=true).
export default defineConfig({
  input: "../../../api/openapi/swing-trade.json",
  output: {
    path: "./src/api/generated",
    postProcess: ["prettier"],
  },
  plugins: ["@hey-api/client-axios"],
});
