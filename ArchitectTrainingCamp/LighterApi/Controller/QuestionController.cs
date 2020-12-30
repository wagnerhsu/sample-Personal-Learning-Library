﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using LighterApi.Data.Question;
using LighterApi.Dto;
using LighterApi.Share;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;

namespace LighterApi.Controller
{
    [ApiController]
    [Route("api/[controller]")]
    public class QuestionController : ControllerBase
    {
        private readonly IMongoCollection<Question> _questionCollection;

        private readonly IMongoCollection<Vote> _voteCollection;

        private readonly IMongoCollection<Answer> _answerCollection;

        public QuestionController(IMongoClient mongoClient)
        {
            var database = mongoClient.GetDatabase("lighter");

            _questionCollection = database.GetCollection<Question>("questions");
            _voteCollection = database.GetCollection<Vote>("votes");
            _answerCollection = database.GetCollection<Answer>("answers");
        }

        [HttpGet]
        [Route("{id}")]
        public async Task<ActionResult<Question>> GetAsync(string id, CancellationToken cancellationToken)
        {
            // linq 查询
            var question = await _questionCollection.AsQueryable()
                .FirstOrDefaultAsync(q => q.Id == id, cancellationToken: cancellationToken);
            if (question == null)
                return NotFound();

            //// mongo 查询表达式
            ////var filter = Builders<Question>.Filter.Eq(q => q.Id, id);

            //// 构造空查询条件的表达式
            //var filter = string.IsNullOrEmpty(id)
            //    ? Builders<Question>.Filter.Empty
            //    : Builders<Question>.Filter.Eq(q => q.Id, id);

            //// 多段拼接 filter
            //var filter2 = Builders<Question>.Filter.And(filter, Builders<Question>.Filter.Eq(q => q.TenantId, "001"));
            //await _questionCollection.Find(filter).FirstOrDefaultAsync(cancellationToken);

            return Ok(question);
        }

        [HttpGet]
        [Route("{id}/answers")]
        public async Task<ActionResult> GetWithAnswerAsync(string id, CancellationToken cancellationToken)
        {
            // linq 查询
            var query = from question in _questionCollection.AsQueryable()
                        where question.Id == id
                        join a in _answerCollection.AsQueryable() on question.Id equals a.QuestionId into answers
                        select new { question, answers };

            var result = await query.FirstOrDefaultAsync(cancellationToken);

            //// mongo 查询表达式
            //var result = await _questionCollection.Aggregate()
            //    .Match(q => q.Id == id)
            //    .Lookup<Answer, QuestionAnswerReponse>(
            //        foreignCollectionName: "answers",
            //        localField: "answers",
            //        foreignField: "questionId",
            //        @as: "AnswerList")
            //    .FirstOrDefaultAsync(cancellationToken: cancellationToken);

            if (result == null)
                return NotFound();

            return Ok(result);
        }

        [HttpGet]
        public async Task<ActionResult<List<Question>>> GetListAsync([FromQuery] List<string> tags,
            CancellationToken cancellationToken, [FromQuery] string sort = "createdAt", [FromQuery] int skip = 0,
            [FromQuery] int limit = 10)
        {
            //// linq 查询
            //await _questionCollection.AsQueryable().Where(q => q.ViewCount > 10)
            //    .ToListAsync(cancellationToken: cancellationToken);

            var filter = Builders<Question>.Filter.Empty;

            if (tags != null && tags.Any())
            {
                filter = Builders<Question>.Filter.AnyIn(q => q.Tags, tags);
            }

            var sortDefinition = Builders<Question>.Sort.Descending(new StringFieldDefinition<Question>(sort));

            var result = await _questionCollection
                .Find(filter)
                .Sort(sortDefinition)
                .Skip(skip)
                .Limit(limit)
                .ToListAsync(cancellationToken: cancellationToken);

            return Ok(result);
        }

        [HttpPost]
        public async Task<ActionResult<Question>> CreateAsync([FromBody] Question question,
            CancellationToken cancellationToken)
        {
            question.Id = Guid.NewGuid().ToString();
            await _questionCollection.InsertOneAsync(question, new InsertOneOptions {BypassDocumentValidation = false},
                cancellationToken);
            return StatusCode((int) HttpStatusCode.Created, question);
        }

        [HttpPatch]
        [Route("{id}")]
        public async Task<ActionResult> UpdateAsync([FromRoute] string id, [FromBody] QuestionUpdateRequest request,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(request.Summary))
                throw new ArgumentNullException(nameof(request.Summary));

            var filter = Builders<Question>.Filter.Eq(q => q.Id, id);

            //var update = Builders<Question>.Update
            //    .Set(q => q.Title, request.Title)
            //    .Set(q => q.Content, request.Content)
            //    .Set(q => q.Tags, request.Tags)
            //    .Push(q => q.Comments, new Comment {Content = request.Summary, CreatedAt = DateTime.Now});

            var updateFieldList = new List<UpdateDefinition<Question>>();

            if (!string.IsNullOrWhiteSpace(request.Title))
                updateFieldList.Add(Builders<Question>.Update.Set(q => q.Title, request.Title));

            if (!string.IsNullOrWhiteSpace(request.Content))
                updateFieldList.Add(Builders<Question>.Update.Set(q => q.Content, request.Content));

            if (request.Tags != null && request.Tags.Any())
                updateFieldList.Add(Builders<Question>.Update.Set(q => q.Tags, request.Tags));

            updateFieldList.Add(Builders<Question>.Update.Push(q => q.Comments,
                new Comment {Content = request.Summary, CreatedAt = DateTime.Now}));

            var update = Builders<Question>.Update.Combine(updateFieldList);

            await _questionCollection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);

            return Ok();
        }

        [HttpPost]
        [Route("{id}/answer")]
        public async Task<ActionResult<Answer>> AnswerAsync([FromRoute] string id, [FromBody] AnswerRequest request,
            CancellationToken cancellationToken)
        {
            var answer = new Answer {QuestionId = id, Content = request.Content, Id = Guid.NewGuid().ToString()};
            _answerCollection.InsertOneAsync(answer, cancellationToken);

            var filter = Builders<Question>.Filter.Eq(q => q.Id, id);
            var update = Builders<Question>.Update.Push(q => q.Answers, answer.Id);

            await _questionCollection.UpdateOneAsync(filter, update, null, cancellationToken);

            return Ok();
        }

        [HttpPost]
        [Route("{id}/comment")]
        public async Task<ActionResult> CommentAsync([FromRoute] string id, [FromBody] CommentRequest request,
            CancellationToken cancellationToken)
        {
            var filter = Builders<Question>.Filter.Eq(q => q.Id, id);
            var update = Builders<Question>.Update.Push(q => q.Comments,
                new Comment {Content = request.Content, CreatedAt = DateTime.Now});

            await _questionCollection.UpdateOneAsync(filter, update, null, cancellationToken);

            return Ok();
        }

        [HttpPost]
        [Route("{id}/up")]
        public async Task<ActionResult> UpAsync([FromBody] string id, CancellationToken cancellationToken)
        {
            var vote = new Vote
            {
                Id = Guid.NewGuid().ToString(),
                SourceType = ConstVoteSourceType.Question,
                SourceId = id,
                Direction = EnumVoteDirection.Up
            };

            await _voteCollection.InsertOneAsync(vote, cancellationToken);

            var filter = Builders<Question>.Filter.Eq(q => q.Id, id);
            var update = Builders<Question>.Update.Inc(q => q.VoteCount, 1).AddToSet(q => q.VoteUps, vote.Id);
            await _questionCollection.UpdateOneAsync(filter, update);

            return Ok();
        }

        [HttpPost]
        [Route("{id}/down")]
        public async Task<ActionResult> DownAsync([FromBody] string id, CancellationToken cancellationToken)
        {
            var vote = new Vote
            {
                Id = Guid.NewGuid().ToString(),
                SourceType = ConstVoteSourceType.Question,
                SourceId = id,
                Direction = EnumVoteDirection.Down
            };

            await _voteCollection.InsertOneAsync(vote, cancellationToken);

            var filter = Builders<Question>.Filter.Eq(q => q.Id, id);
            var update = Builders<Question>.Update.Inc(q => q.VoteCount, -1).AddToSet(q => q.VoteDowns, vote.Id);
            await _questionCollection.UpdateOneAsync(filter, update);

            return Ok();
        }
    }
}
