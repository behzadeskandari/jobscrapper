using jobscrapper.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace JobMatchApi.Controllers
{
    [ApiController]
    [Route("api/ml")]
    public class MLController : ControllerBase
    {
        private readonly IJobInferenceService _ml;

        public MLController(IJobInferenceService ml)
        {
            _ml = ml;
        }

        [HttpPost("train")]
        public IActionResult Train([FromBody] string jsonlPath)
        {
            var data = _ml.LoadJsonl(jsonlPath);

            _ml.TrainCategoryModel(data);
            _ml.TrainAnswerModels(data);

            _ml.SaveCategoryAndAnswerModels("models");

            return Ok("Training completed!");
        }

        [HttpPost("load")]
        public IActionResult LoadModels()
        {
            _ml.LoadCategoryModel("models/category.zip");
            _ml.LoadAnswerModels("models");

            return Ok("Models loaded!");
        }

        [HttpGet("predict")]
        public IActionResult Predict(string q)
        {
            var cat = _ml.PredictJobCategory(q);
            var ans = _ml.PredictAnswer(q);

            return Ok(new { category = cat, answer = ans });
        }
    }
}
