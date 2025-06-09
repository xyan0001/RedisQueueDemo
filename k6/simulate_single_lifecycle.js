import http from "k6/http";
import { check } from "k6";

export let options = {
  scenarios: {
    constant_arrival: {
      executor: "constant-arrival-rate",
      rate: 10000, // 10,000 iterations per minute
      timeUnit: "1m",
      duration: "1m",
      preAllocatedVUs: 20, // adjust as needed for your environment
      maxVUs: 500, // upper bound for scaling
    },
  },
  thresholds: {
    http_req_duration: ["p(95)<500"],
    http_req_failed: ["rate<0.01"],
  },
};

const BASE_URL = __ENV.BASE_URL || "http://localhost:5000";

export default function () {
  let res = http.get(`${BASE_URL}/api/terminals/simulate-single-lifecycle`);
  check(res, {
    "status is 200": (r) => r.status === 200,
    "simulation completed": (r) => {
      const body = r.json();
      return body.message && body.message.includes("completed");
    },
  });
}
