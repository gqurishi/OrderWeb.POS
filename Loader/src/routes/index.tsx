import { createFileRoute } from "@tanstack/react-router";
import { ChefLoading } from "@/components/chef-loading";

export const Route = createFileRoute("/")({
  head: () => ({
    meta: [
      { title: "ChefLoader — Restaurant POS Loading" },
      {
        name: "description",
        content:
          "A lightweight, reusable cartoon chef loading component for restaurant POS apps.",
      },
      { property: "og:title", content: "ChefLoader — Restaurant POS Loading" },
      {
        property: "og:description",
        content:
          "A lightweight, reusable cartoon chef loading component for restaurant POS apps.",
      },
      { property: "og:type", content: "website" },
      { name: "twitter:card", content: "summary_large_image" },
    ],
  }),
  component: DemoPage,
});

function DemoPage() {
  return (
    <main className="flex min-h-screen items-center justify-center bg-[#f6f8fc] px-6 py-16">
      <ChefLoading mode="inline" size="lg" />
    </main>
  );
}
