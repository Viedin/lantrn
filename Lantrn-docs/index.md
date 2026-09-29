---
layout: home

hero:
  name: "Lantrn"
  text: "Search your documents by meaning"
  tagline: Self-hosted. Bring your own models. Your files never have to leave your network.
  image:
    src: /logo.svg
    alt: Lantrn
  actions:
    - theme: brand
      text: Get started
      link: /guide/getting-started
    - theme: alt
      text: Configuration
      link: /guide/configuration

features:
  - title: Hybrid search
    details: Meaning and keywords in one ranking. Finds the idea, and still catches exact names, codes and amounts.
  - title: Any document
    details: PDF, Markdown, HTML, text — and images, read by a vision model. Or crawl a whole website.
  - title: Answers with sources
    details: Optionally let a chat model answer from the results, citing every passage it used.
  - title: Bring any model
    details: Works with any OpenAI-compatible API — OpenAI, LM Studio, Ollama, vLLM.
  - title: Private or shared
    details: Keep a collection to yourself, or make it public so everyone can search it and add to it.
  - title: Built for teams
    details: Admins, users and invite links. Or turn on public search and share it with everyone.
---

## Up and running in a minute
Download the compose file into a new folder and start it:

```sh
curl -O https://raw.githubusercontent.com/Viedin/lantrn/main/Lantrn/docker-compose.yml
docker compose up -d
```

1. Open http://localhost:8080 and register — the first account is the admin.
2. Point **Settings → Embeddings** at your model.
3. Create a collection, upload files on the **Ingest** page, and start searching.

See [Adding documents](/guide/adding-documents) for all the options.

[Read the full guide →](/guide/getting-started)
