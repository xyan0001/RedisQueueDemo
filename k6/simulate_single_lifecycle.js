import http from "k6/http";
import { check, sleep } from "k6";

export let options = {
  vus: 30, // Number of virtual users (match terminal pool size)
  duration: "1m", // Test duration
  thresholds: {
    http_req_duration: ["p(95)<500"],
    http_req_failed: ["rate<0.01"],
  },
};

const BASE_URL = __ENV.BASE_URL || "http://localhost:5000";

export default function () {
  // Test the /simulate-single-lifecycle endpoint
  let res = http.get(`${BASE_URL}/api/terminals/simulate-single-lifecycle`);
  check(res, {
    "status is 200": (r) => r.status === 200,
    "simulation completed": (r) => {
      const body = r.json();
      return body.message && body.message.includes("completed");
    },
  });
  //sleep(Math.random() * 2 + 1); // Simulate user think time
}
