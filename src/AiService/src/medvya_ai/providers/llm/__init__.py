from medvya_ai.config import Settings
from medvya_ai.providers.llm.base import LLMClient
from medvya_ai.providers.llm.fake import FakeLLM
from medvya_ai.providers.llm.foundry import FoundryLLM


def build_llm(settings: Settings) -> LLMClient:
    if settings.llm_provider == "fake":
        return FakeLLM()
    if not (
        settings.foundry_endpoint
        and settings.foundry_api_key
        and settings.foundry_deployment_extract
        and settings.foundry_deployment_normalise
    ):
        raise RuntimeError(
            "FOUNDRY_ENDPOINT, FOUNDRY_API_KEY and both FOUNDRY_DEPLOYMENT_* are required "
            "when LLM_PROVIDER=foundry"
        )
    return FoundryLLM(
        endpoint=settings.foundry_endpoint,
        api_key=settings.foundry_api_key.get_secret_value(),
        deployments={
            "normalise": settings.foundry_deployment_normalise,
            "extract": settings.foundry_deployment_extract,
            "receptionist": settings.foundry_deployment_receptionist
            or settings.foundry_deployment_extract,
        },
    )
